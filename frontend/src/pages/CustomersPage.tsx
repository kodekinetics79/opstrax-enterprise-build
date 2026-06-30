import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { customersApi } from "@/services/customersApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Helpers ──────────────────────────────────────────────────────────────────

function RiskBadge({ level }: { level: string }) {
  const cls =
    level === "High" ? "bg-red-50 border-red-200 text-red-700" :
    level === "Medium" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-teal-50 border-teal-200 text-teal-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{level}</span>;
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "At Risk" ? "bg-red-50 border-red-200 text-red-700" :
    status === "Inactive" ? "bg-slate-100 border-slate-200 text-slate-600" :
    "bg-amber-50 border-amber-200 text-amber-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function SlaTierBadge({ tier }: { tier: string }) {
  const cls =
    tier === "Platinum" ? "bg-violet-50 border-violet-200 text-violet-700" :
    tier === "Gold" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{tier}</span>;
}

function ScoreBar({ score, thresholdHigh = 90, thresholdMid = 80 }: { score: number; thresholdHigh?: number; thresholdMid?: number }) {
  const pct = Math.min(100, Math.max(0, score));
  const color = pct >= thresholdHigh ? "bg-teal-500" : pct >= thresholdMid ? "bg-amber-400" : "bg-red-400";
  return (
    <div className="flex items-center gap-2 text-xs">
      <div className="w-20 h-1.5 rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="w-7 text-right text-slate-700 font-medium">{Math.round(pct)}</span>
    </div>
  );
}

// ── Add Customer Modal ────────────────────────────────────────────────────────

function AddCustomerModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ name: "", customerCode: "", contactName: "", email: "", phone: "", slaTier: "Standard" });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => customersApi.create(form as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["customers"] }); onSaved(); },
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-lg p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">Add Customer</h2>
        <div className="grid grid-cols-2 gap-3">
          {[
            { label: "Company Name*", key: "name", placeholder: "Gulf Express Logistics" },
            { label: "Customer Code*", key: "customerCode", placeholder: "CUS-001" },
            { label: "Primary Contact", key: "contactName", placeholder: "Jane Doe" },
            { label: "Email", key: "email", placeholder: "ops@company.com" },
            { label: "Phone", key: "phone", placeholder: "+966 11 XXX XXXX" },
          ].map(({ label, key, placeholder }) => (
            <div key={key} className={key === "name" ? "col-span-2" : ""}>
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
            <label className="block text-xs font-medium text-slate-600 mb-1">SLA Tier</label>
            <select
              title="SLA Tier"
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.slaTier}
              onChange={(e) => setForm((f) => ({ ...f, slaTier: e.target.value }))}
            >
              {["Standard", "Gold", "Platinum"].map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button
            type="button"
            disabled={!form.name || !form.customerCode || mut.isPending}
            className="btn-primary text-sm"
            onClick={() => mut.mutate()}
          >
            {mut.isPending ? "Saving…" : "Add Customer"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type StatusFilter = "All" | "Active" | "At Risk" | "Inactive";

export function CustomersPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");
  const [tierFilter, setTierFilter] = useState<"All" | "Platinum" | "Gold" | "Standard">("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showAdd, setShowAdd] = useState(false);

  const listQ = useQuery({ queryKey: ["customers", "list"], queryFn: customersApi.list, refetchInterval: 30_000 });
  const sumQ = useQuery({ queryKey: ["customers", "summary"], queryFn: customersApi.summary });
  const detailQ = useQuery({
    queryKey: ["customers", "detail", selected?.id],
    queryFn: () => customersApi.detail(selected!.id as string | number),
    enabled: selected != null,
  });
  const qc = useQueryClient();

  const customers = (listQ.data ?? []) as AnyRecord[];
  const s = (sumQ.data ?? {}) as AnyRecord;
  const detail = (detailQ.data ?? {}) as AnyRecord;

  const filtered = customers.filter((c) => {
    if (statusFilter !== "All" && c.status !== statusFilter) return false;
    if (tierFilter !== "All" && c.slaTier !== tierFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(c.name ?? "").toLowerCase().includes(q) ||
        String(c.customerCode ?? "").toLowerCase().includes(q) ||
        String(c.contactName ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {showAdd && (
        <AddCustomerModal
          onClose={() => setShowAdd(false)}
          onSaved={() => setShowAdd(false)}
        />
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Customers</h1>
          <p className="text-sm text-slate-500 mt-0.5">Customer accounts, SLA health, delivery experience, and risk monitoring</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("customers", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowAdd(true)}>Add Customer</button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Customers",         val: s.total ?? customers.length },
          { label: "Active",                  val: s.active ?? customers.filter((c) => c.status === "Active").length, accent: "text-teal-600" },
          { label: "At Risk",                 val: s.atRisk ?? customers.filter((c) => c.status === "At Risk").length, accent: "text-red-600" },
          { label: "Avg SLA Health",          val: `${s.slaHealthScore ?? "--"}%`, accent: "text-violet-600" },
          { label: "Avg Delivery Experience", val: `${s.deliveryExperienceScore ?? "--"}%`, accent: "text-teal-600" },
          { label: "Platinum Accounts",       val: s.platinumAccounts ?? customers.filter((c) => c.slaTier === "Platinum").length, accent: "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Active", "At Risk", "Inactive"] as StatusFilter[]).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {f}
            </button>
          ))}
        </div>
        <select
          title="SLA Tier filter"
          value={tierFilter}
          onChange={(e) => setTierFilter(e.target.value as typeof tierFilter)}
          className="border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
        >
          <option value="All">All Tiers</option>
          <option value="Platinum">Platinum</option>
          <option value="Gold">Gold</option>
          <option value="Standard">Standard</option>
        </select>
        <input
          type="search"
          placeholder="Search by name, code, contact…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56"
        />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No customers match your filters" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Customer", "Status", "SLA Tier", "SLA Health", "Delivery Exp.", "Risk", "Active Jobs", "Action"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((c, i) => (
                  <tr
                    key={String(c.id ?? i)}
                    className={`hover:bg-slate-50 cursor-pointer ${selected?.id === c.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === c.id ? null : c)}
                  >
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(c.name ?? "--")}</p>
                      <p className="text-xs text-slate-400">{String(c.customerCode ?? "")} · {String(c.contactName ?? "")}</p>
                    </td>
                    <td className="px-4 py-3"><StatusBadge status={String(c.status ?? "Active")} /></td>
                    <td className="px-4 py-3"><SlaTierBadge tier={String(c.slaTier ?? "Standard")} /></td>
                    <td className="px-4 py-3"><ScoreBar score={Number(c.slaHealthScore ?? 0)} /></td>
                    <td className="px-4 py-3"><ScoreBar score={Number(c.deliveryExperienceScore ?? c.customerDeliveryExperienceScore ?? 0)} /></td>
                    <td className="px-4 py-3"><RiskBadge level={String(c.riskHeatScore ?? "Low")} /></td>
                    <td className="px-4 py-3 text-slate-700">
                      {Number(c.activeJobs ?? 0) > 0 ? (
                        <span className="text-xs font-semibold text-teal-700">{String(c.activeJobs)}</span>
                      ) : (
                        <span className="text-slate-400 text-xs">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {c.recommendedAction ? (
                        <span className="text-xs text-slate-600 italic">{String(c.recommendedAction)}</span>
                      ) : (
                        <span className="text-slate-400 text-xs">—</span>
                      )}
                    </td>
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
              <span className="text-sm font-semibold text-white">{String(selected.name)}</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6 flex gap-2 flex-wrap">
              <StatusBadge status={String(selected.status ?? "Active")} />
              <SlaTierBadge tier={String(selected.slaTier ?? "Standard")} />
              <RiskBadge level={String(selected.riskHeatScore ?? "Low")} />
            </div>
            <div className="px-5 py-4 flex flex-col gap-3 border-b border-white/6">
              <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide">Scores</p>
              <div className="flex items-center gap-2 text-xs">
                <span className="w-28 text-slate-400 shrink-0">SLA Health</span>
                <ScoreBar score={Number(selected.slaHealthScore ?? 0)} />
              </div>
              <div className="flex items-center gap-2 text-xs">
                <span className="w-28 text-slate-400 shrink-0">Delivery Exp.</span>
                <ScoreBar score={Number(selected.deliveryExperienceScore ?? selected.customerDeliveryExperienceScore ?? 0)} />
              </div>
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Code", String(selected.customerCode ?? "")],
                ["Contact", String(selected.contactName ?? "—")],
                ["Email", String(selected.email ?? "—")],
                ["Phone", String(selected.phone ?? "—")],
                ["Active Jobs", String(selected.activeJobs ?? 0)],
                ["Risk Score", String(selected.riskScore ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5 break-all">{String(v)}</p>
                </div>
              ))}
            </div>
            {!!selected.recommendedAction && (
              <div className="px-5 py-4">
                <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Recommended Action</p>
                <p className="text-sm text-slate-300 leading-relaxed">{String(selected.recommendedAction)}</p>
              </div>
            )}
            {!!selected.billingAddress && (
              <div className="px-5 pb-4">
                <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-1">Billing Address</p>
                <p className="text-xs text-slate-300">{String(selected.billingAddress)}</p>
              </div>
            )}
            <div className="px-5 py-4 border-t border-white/6">
              <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-2">Operational Sites</p>
              {(detail.sites as AnyRecord[] | undefined)?.length ? (
                <div className="space-y-2">
                  {(detail.sites as AnyRecord[]).slice(0, 4).map((site) => (
                    <div key={String(site.id)} className="rounded-lg border border-white/10 bg-white/5 p-3">
                      <p className="text-sm font-semibold text-white">{String(site.siteName ?? site.site_code ?? "Site")}</p>
                      <p className="text-xs text-slate-300 mt-1">{String(site.siteType ?? site.site_type ?? "service")} · {String(site.status ?? "Active")}</p>
                      <p className="text-xs text-slate-400 mt-1">{String(site.city ?? "")} {String(site.state ?? "")}</p>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-xs text-slate-400">No operational sites captured yet.</p>
              )}
            </div>
            <div className="px-5 py-4 border-t border-white/6 mt-auto">
              <button
                type="button"
                className="w-full text-sm px-3 py-2 rounded-lg bg-teal-600 hover:bg-teal-700 text-white font-medium transition-colors"
                onClick={() => {
                  void qc.invalidateQueries({ queryKey: ["customers"] });
                  setSelected(null);
                }}
              >
                Refresh Data
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
