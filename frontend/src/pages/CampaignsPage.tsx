import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { requireCommercialModuleRecords } from "@/services/commercialModulePayload";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

function persistedNumber(value: unknown): number | null {
  if (value == null || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

const campaignsApi = {
  list: () => unwrap<unknown>(apiClient.get("/api/campaigns")).then((payload) =>
    requireCommercialModuleRecords(payload, "campaigns").map((r) => ({
      ...r,
      campaignName: r.campaignName ?? r.title ?? "",
      segment: r.segment ?? "",
      channel: r.channel ?? "",
      audienceSize: persistedNumber(r.audienceSize),
      openRate: r.openRate ?? "—",
      responseRate: r.responseRate ?? "—",
      leadsGenerated: persistedNumber(r.leadsGenerated),
      revenueInfluenced: persistedNumber(r.revenueInfluenced),
      currency: r.currency ?? "",
      startDate: r.startDate ?? r.dueAt ?? "",
    }))
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

  const measuredLeadRows = campaigns.filter((campaign) => campaign.leadsGenerated != null);
  const totalLeads = measuredLeadRows.reduce((sum, campaign) => sum + Number(campaign.leadsGenerated), 0);
  const measuredRevenueRows = campaigns.filter((campaign) => campaign.revenueInfluenced != null && campaign.currency);
  const revenueCurrencies = [...new Set(measuredRevenueRows.map((campaign) => String(campaign.currency)))];
  const totalRevenue = measuredRevenueRows.reduce((sum, campaign) => sum + Number(campaign.revenueInfluenced), 0);
  const revenueLabel = revenueCurrencies.length === 1
    ? `${revenueCurrencies[0]} ${totalRevenue.toLocaleString()}`
    : revenueCurrencies.length > 1 ? "Multiple currencies" : "—";
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
    <div className="page-stack min-w-0">
      {showCreate && <CreateCampaignModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Campaigns</h1>
          <p className="text-sm text-slate-500 mt-0.5">Persisted campaign register with recorded targeting and performance evidence</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("campaigns", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Campaign</button>
        </div>
      </div>

      <div className="panel grid gap-3 md:grid-cols-2">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Attribution boundary</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Campaign-to-lead creation and revenue attribution are not automated in this build.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Evidence rule</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Audience, engagement, leads and revenue remain unavailable until persisted measurements exist.</p>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Campaigns",     val: campaigns.length },
          { label: "Active",              val: active, accent: "text-teal-600" },
          { label: "Leads Generated",     val: measuredLeadRows.length ? totalLeads : "—", accent: "text-blue-600" },
          { label: "Revenue Influenced",  val: revenueLabel, accent: "text-violet-600" },
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
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
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
                    <td className="px-4 py-3 text-slate-700">{c.audienceSize == null ? "—" : Number(c.audienceSize).toLocaleString()}</td>
                    <td className="px-4 py-3 font-medium text-slate-700">{String(c.openRate ?? "—")}</td>
                    <td className="px-4 py-3 font-medium text-teal-700">{String(c.responseRate ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(c.leadsGenerated ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700 text-xs">
                      {c.revenueInfluenced != null && c.currency
                        ? `${String(c.currency)} ${Number(c.revenueInfluenced).toLocaleString()}`
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
                ["Audience", selected.audienceSize == null ? "—" : Number(selected.audienceSize).toLocaleString()],
                ["Open Rate", String(selected.openRate ?? "—")],
                ["Response Rate", String(selected.responseRate ?? "—")],
                ["Leads Generated", String(selected.leadsGenerated ?? "—")],
                ["Revenue Influenced", selected.revenueInfluenced == null || !selected.currency ? "—" : `${String(selected.currency)} ${Number(selected.revenueInfluenced).toLocaleString()}`],
                ["Start Date", String(selected.startDate ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Performance status</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.status) === "Active" && selected.responseRate !== "—"
                  ? `Recorded response rate: ${String(selected.responseRate)}. Review the source measurement before changing spend or targeting.`
                  : String(selected.status) === "Scheduled"
                  ? "Campaign is queued. Verify audience list and creative assets before launch."
                  : "No persisted response measurement is available for this campaign."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
