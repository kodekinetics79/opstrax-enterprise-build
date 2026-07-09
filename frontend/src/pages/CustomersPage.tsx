import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState, KpiCard, Select } from "@/components/ui";
import { customers as seedCustomers } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";
import { Building2, Download, Plus, Search, ShieldAlert, Sparkles, TrendingUp, Users } from "lucide-react";

// ── Seed ────────────────────────────────────────────────────────────────────

function buildSeed(): AnyRecord[] {
  return (seedCustomers as AnyRecord[]).map((c, i) => ({
    id: i + 1,
    customerCode: String(c.id ?? `CUS-${i + 1}`),
    name: String(c.companyName ?? c.name ?? ""),
    contactName: String(c.primaryContact ?? ""),
    email: String(c.email ?? `ops@customer${i + 1}.com`),
    phone: "",
    status: String(c.status ?? "Active") === "Healthy" ? "Active" :
            String(c.status ?? "Active") === "At Risk" ? "At Risk" :
            String(c.status ?? "Active") === "High Risk" ? "At Risk" : String(c.status ?? "Active"),
    slaTier: (["Platinum", "Gold", "Standard", "Standard", "Platinum", "Gold"] as const)[i % 6],
    slaHealthScore: Number(c.healthScore ?? 88),
    deliveryExperienceScore: Number(c.healthScore ?? 85) - 2,
    riskScore: 100 - Number(c.healthScore ?? 80),
    activeJobs: Number(c.monthlyShipments ?? 10),
    customerDeliveryExperienceScore: Number(c.healthScore ?? 85),
    riskHeatScore: Number(c.healthScore ?? 80) < 75 ? "High" : Number(c.healthScore ?? 80) < 88 ? "Medium" : "Low",
    recommendedAction: Number(c.healthScore ?? 80) < 80 ? "Send proactive customer update" : "Maintain SLA cadence",
  }));
}

const SEED_SUMMARY: AnyRecord = {
  total: 6, active: 4, atRisk: 2, slaHealthScore: 85.3, deliveryExperienceScore: 83.8, platinumAccounts: 2,
};

const customersApi = {
  list: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/customers")), () => buildSeed()),
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/customers/summary")), () => SEED_SUMMARY),
  create: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/customers", body)),
};

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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in" onClick={onClose}>
      <div className="panel w-full max-w-lg flex flex-col gap-4 overflow-hidden" onClick={(e) => e.stopPropagation()}>
        <div className="border-b border-slate-200 pb-4">
          <h2 className="section-title text-slate-900">Add Customer</h2>
        </div>
        <div className="grid grid-cols-2 gap-3 px-2">
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
            <Select
              title="SLA Tier"
              className="w-full"
              value={form.slaTier}
              onChange={(e) => setForm((f) => ({ ...f, slaTier: e.target.value }))}
            >
              {["Standard", "Gold", "Platinum"].map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600 px-2">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 border-t border-slate-200 pt-4">
          <button type="button" className="fh-btn-ghost cursor-pointer text-sm" onClick={onClose}>Cancel</button>
          <button
            type="button"
            disabled={!form.name || !form.customerCode || mut.isPending}
            className="fh-btn-primary cursor-pointer text-sm disabled:opacity-50"
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
  const qc = useQueryClient();

  const customers = (listQ.data ?? []) as AnyRecord[];
  const s = (sumQ.data ?? {}) as AnyRecord;

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
    <div className="space-y-6 pb-10">
      {showAdd && (
        <AddCustomerModal
          onClose={() => setShowAdd(false)}
          onSaved={() => setShowAdd(false)}
        />
      )}

      {/* ── Hero header ─────────────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Users className="h-3 w-3" /> CRM Platform
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Customer accounts and SLA health monitoring</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Customer Relationship Hub
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Monitor customer accounts, SLA health, delivery experience, and risk signals in one operational surface.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" className="fh-btn-ghost cursor-pointer" onClick={() => exportCsv("customers", filtered)}><Download className="h-4 w-4" /> Export CSV</button>
              <button type="button" className="fh-btn-primary cursor-pointer" onClick={() => setShowAdd(true)}><Plus className="h-4 w-4" /> Add Customer</button>
            </div>
          </div>
        </div>
      </header>

      {/* ── Ops intelligence bar ────────────────────────────────────── */}
      <div className="anim-fade-up relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-indigo-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
            <Sparkles className="h-5 w-5 text-teal-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Customer health signal</p>
            <p className="mt-1 text-sm font-medium leading-relaxed text-slate-400">
              {Number(s.atRisk ?? 0) === 0
                ? "All customer accounts are healthy — no SLA or delivery experience exceptions."
                : `${s.atRisk ?? customers.filter((c) => c.status === "At Risk").length} at-risk accounts need attention`}
            </p>
          </div>
        </div>
        {Number(s.atRisk ?? 0) > 0 && (
          <button type="button" onClick={() => setStatusFilter("At Risk")} className="cursor-pointer inline-flex items-center gap-2 self-start rounded-xl bg-gradient-to-r from-teal-500 to-teal-600 px-4 py-2.5 text-xs font-bold text-white shadow-lg shadow-teal-500/20 transition hover:from-teal-400 hover:to-teal-500 hover:shadow-teal-400/30 sm:self-auto">
            Review at-risk accounts <TrendingUp className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* ── KPI cards ───────────────────────────────────────────────── */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        <KpiCard label="Total Customers" value={String(s.total ?? customers.length)} icon={<Building2 className="h-4 w-4" />} />
        <KpiCard label="Active" value={String(s.active ?? customers.filter((c) => c.status === "Active").length)} icon={<Users className="h-4 w-4" />} />
        <KpiCard label="At Risk" value={String(s.atRisk ?? customers.filter((c) => c.status === "At Risk").length)} icon={<ShieldAlert className="h-4 w-4" />} status="review" />
        <KpiCard label="Avg SLA Health" value={`${s.slaHealthScore ?? "--"}%`} icon={<TrendingUp className="h-4 w-4" />} status="review" />
        <KpiCard label="Delivery Exp." value={`${s.deliveryExperienceScore ?? "--"}%`} icon={<TrendingUp className="h-4 w-4" />} />
        <KpiCard label="Platinum Accounts" value={String(s.platinumAccounts ?? customers.filter((c) => c.slaTier === "Platinum").length)} icon={<ShieldAlert className="h-4 w-4" />} status="review" />
      </div>

      {/* ── Status filter chips ─────────────────────────────────────── */}
      <div className="panel p-2">
        <div className="grid grid-cols-2 gap-1.5 sm:grid-cols-4">
          {(["All", "Active", "At Risk", "Inactive"] as StatusFilter[]).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`flex flex-col items-start rounded-xl border px-3 py-2.5 text-left transition cursor-pointer ${
                statusFilter === f
                  ? "bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                  : "border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50"
              }`}
            >
              <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{f}</span>
              <span className={`mt-0.5 text-xl font-bold tabular-nums ${statusFilter === f ? "text-teal-700" : "text-slate-900"}`}>
                {f === "All" ? customers.length : customers.filter((c) => c.status === f).length}
              </span>
            </button>
          ))}
        </div>
      </div>

      {/* ── Toolbar ─────────────────────────────────────────────────── */}
      <div className="panel flex flex-col gap-3 p-3.5 lg:flex-row lg:items-center">
        <div className="relative min-w-[220px] flex-1 lg:max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 shrink-0 -translate-y-1/2 text-slate-400" />
          <input className="field h-10 pl-9 cursor-pointer" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search customers, codes, contacts..." />
        </div>
        <Select
          title="SLA Tier filter"
          value={tierFilter}
          onChange={(e) => setTierFilter(e.target.value as typeof tierFilter)}
          className="lg:max-w-[180px] cursor-pointer"
        >
          <option value="All">All Tiers</option>
          <option value="Platinum">Platinum</option>
          <option value="Gold">Gold</option>
          <option value="Standard">Standard</option>
        </Select>
        <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-500">{filtered.length} of {customers.length} customers</span>
      </div>

      {/* ── Table ───────────────────────────────────────────────────── */}
      {filtered.length === 0 ? (
        <EmptyState title="No customers match your filters" subtitle="Adjust the status, tier, or search to widen results." />
      ) : (
        <div className="overflow-x-auto rounded-xl border border-slate-200">
          <table className="w-full min-w-[900px] text-left text-sm">
            <thead className="bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-500">
              <tr className="border-b border-slate-200">
                <th className="px-4 py-2.5">Customer</th>
                <th className="px-4 py-2.5">Status</th>
                <th className="px-4 py-2.5">SLA Tier</th>
                <th className="px-4 py-2.5">SLA Health</th>
                <th className="px-4 py-2.5">Delivery Exp.</th>
                <th className="px-4 py-2.5">Risk</th>
                <th className="px-4 py-2.5">Active Jobs</th>
                <th className="px-4 py-2.5">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {filtered.map((c, i) => (
                <tr
                  key={String(c.id ?? i)}
                  className="cursor-pointer transition-colors hover:bg-slate-50"
                  onClick={() => setSelected(selected?.id === c.id ? null : c)}
                >
                  <td className="px-4 py-2.5">
                    <p className="font-medium text-slate-900">{String(c.name ?? "--")}</p>
                    <p className="text-xs text-slate-400">{String(c.customerCode ?? "")} · {String(c.contactName ?? "")}</p>
                  </td>
                  <td className="px-4 py-2.5"><StatusBadge status={String(c.status ?? "Active")} /></td>
                  <td className="px-4 py-2.5"><SlaTierBadge tier={String(c.slaTier ?? "Standard")} /></td>
                  <td className="px-4 py-2.5"><ScoreBar score={Number(c.slaHealthScore ?? 0)} /></td>
                  <td className="px-4 py-2.5"><ScoreBar score={Number(c.deliveryExperienceScore ?? c.customerDeliveryExperienceScore ?? 0)} /></td>
                  <td className="px-4 py-2.5"><RiskBadge level={String(c.riskHeatScore ?? "Low")} /></td>
                  <td className="px-4 py-2.5">
                    {Number(c.activeJobs ?? 0) > 0 ? (
                      <span className="text-xs font-semibold text-teal-700">{String(c.activeJobs)}</span>
                    ) : (
                      <span className="text-slate-400 text-xs">—</span>
                    )}
                  </td>
                  <td className="px-4 py-2.5">
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

      {/* ── Detail drawer ───────────────────────────────────────────── */}
      {selected && (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-sm anim-fade-in">
          <aside className="anim-slide-right flex h-full w-full max-w-lg flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="section-title text-teal-700">Customer Detail</p>
                  <h2 className="mt-1 text-2xl font-bold text-slate-900">{String(selected.name)}</h2>
                  <div className="mt-3 flex flex-wrap items-center gap-2">
                    <StatusBadge status={String(selected.status ?? "Active")} />
                    <SlaTierBadge tier={String(selected.slaTier ?? "Standard")} />
                    <RiskBadge level={String(selected.riskHeatScore ?? "Low")} />
                  </div>
                </div>
                <button type="button" className="icon-btn cursor-pointer" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
              </div>
            </div>
            <div className="space-y-6 px-6 py-6">
              <section className="rounded-2xl border border-teal-100 bg-teal-50/50 p-4">
                <h3 className="section-title text-teal-800">Performance Scores</h3>
                <div className="mt-3 space-y-2.5">
                  <div className="flex items-center gap-2 text-xs">
                    <span className="w-28 shrink-0 text-slate-500">SLA Health</span>
                    <ScoreBar score={Number(selected.slaHealthScore ?? 0)} />
                  </div>
                  <div className="flex items-center gap-2 text-xs">
                    <span className="w-28 shrink-0 text-slate-500">Delivery Exp.</span>
                    <ScoreBar score={Number(selected.deliveryExperienceScore ?? selected.customerDeliveryExperienceScore ?? 0)} />
                  </div>
                </div>
              </section>

              <section className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <h3 className="section-title">Account Details</h3>
                <div className="mt-3 space-y-2.5">
                  {[
                    ["Code", String(selected.customerCode ?? "")],
                    ["Contact", String(selected.contactName ?? "—")],
                    ["Email", String(selected.email ?? "—")],
                    ["Phone", String(selected.phone ?? "—")],
                    ["Active Jobs", String(selected.activeJobs ?? 0)],
                    ["Risk Score", String(selected.riskScore ?? "—")],
                  ].map(([k, v]) => (
                    <div key={String(k)} className="flex items-start justify-between gap-3">
                      <span className="text-xs font-medium text-slate-500">{String(k)}</span>
                      <span className="text-right text-sm font-medium text-slate-800 break-all">{String(v)}</span>
                    </div>
                  ))}
                </div>
              </section>

              {!!selected.recommendedAction && (
                <section>
                  <h3 className="section-title">Recommended Action</h3>
                  <p className="mt-2 rounded-xl border border-dashed border-teal-200 bg-teal-50 px-4 py-3 text-sm text-slate-600">{String(selected.recommendedAction)}</p>
                </section>
              )}

              {!!selected.billingAddress && (
                <section>
                  <h3 className="section-title">Billing Address</h3>
                  <p className="mt-2 text-sm text-slate-600">{String(selected.billingAddress)}</p>
                </section>
              )}
            </div>
            <div className="mt-auto border-t border-slate-200 px-6 py-4">
              <button
                type="button"
                className="fh-btn-primary w-full cursor-pointer justify-center"
                onClick={() => {
                  void qc.invalidateQueries({ queryKey: ["customers"] });
                  setSelected(null);
                }}
              >
                Refresh Data
              </button>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
