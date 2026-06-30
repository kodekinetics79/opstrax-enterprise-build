import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { contractsApi } from "@/services/contractsApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Helpers ──────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Expiring Soon" ? "bg-amber-50 border-amber-200 text-amber-700" :
    status === "Expired" ? "bg-red-50 border-red-200 text-red-700" :
    status === "Under Renewal" ? "bg-blue-50 border-blue-200 text-blue-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function RiskBadge({ level }: { level: string }) {
  const cls =
    level === "High" ? "bg-red-50 border-red-200 text-red-700" :
    level === "Medium" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-teal-50 border-teal-200 text-teal-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{level}</span>;
}

function fmtDate(d: unknown): string {
  if (!d) return "—";
  try { return new Date(String(d)).toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" }); }
  catch { return String(d); }
}

// ── Create Contract Modal ────────────────────────────────────────────────────

function CreateContractModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ contractCode: "", title: "", rateType: "FTL", effectiveDate: "", expiryDate: "" });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => contractsApi.create(form as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["contracts"] }); onSaved(); },
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-lg p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">Create Contract</h2>
        <div className="grid grid-cols-2 gap-3">
          {[
            { label: "Contract Code*", key: "contractCode", placeholder: "CON-2001" },
            { label: "Title*", key: "title", placeholder: "FTL Service Agreement" },
            { label: "Effective Date", key: "effectiveDate", placeholder: "2026-01-01", type: "date" },
            { label: "Expiry Date", key: "expiryDate", placeholder: "2027-01-01", type: "date" },
          ].map(({ label, key, placeholder, type }) => (
            <div key={key} className={key === "title" ? "col-span-2" : ""}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input
                type={type ?? "text"}
                className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
                placeholder={placeholder}
                value={String(form[key as keyof typeof form])}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))}
              />
            </div>
          ))}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Rate Type</label>
            <select
              title="Rate Type"
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.rateType}
              onChange={(e) => setForm((f) => ({ ...f, rateType: e.target.value }))}
            >
              {["FTL", "LTL", "Per KM", "Fixed Trip", "Zone Based", "Cold Chain"].map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button
            type="button"
            disabled={!form.contractCode || !form.title || mut.isPending}
            className="btn-primary text-sm"
            onClick={() => mut.mutate()}
          >
            {mut.isPending ? "Saving…" : "Create Contract"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type StatusFilter = "All" | "Active" | "Expiring Soon" | "Expired" | "Under Renewal";

export function ContractsPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");
  const [riskFilter, setRiskFilter] = useState<"All" | "High" | "Medium" | "Low">("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const qc = useQueryClient();

  const listQ = useQuery({ queryKey: ["contracts", "list"], queryFn: contractsApi.list, refetchInterval: 30_000 });
  const sumQ = useQuery({ queryKey: ["contracts", "summary"], queryFn: contractsApi.summary });
  const detailQ = useQuery({
    queryKey: ["contracts", "detail", selected?.id],
    queryFn: () => contractsApi.detail(selected!.id as string | number),
    enabled: selected != null,
  });

  const activateMut = useMutation({
    mutationFn: (id: string | number) => contractsApi.activate(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["contracts"] }),
  });

  const contracts = (listQ.data ?? []) as AnyRecord[];
  const s = (sumQ.data ?? {}) as AnyRecord;
  const detail = (detailQ.data ?? {}) as AnyRecord;

  const filtered = contracts.filter((c) => {
    if (statusFilter !== "All" && c.displayStatus !== statusFilter && c.status !== statusFilter) return false;
    if (riskFilter !== "All" && c.riskHeatScore !== riskFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(c.contractCode ?? "").toLowerCase().includes(q) ||
        String(c.title ?? "").toLowerCase().includes(q) ||
        String(c.customerName ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {showCreate && (
        <CreateContractModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Contracts</h1>
          <p className="text-sm text-slate-500 mt-0.5">Customer and carrier contract health, margin risk, renewal queue, and rate oversight</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("contracts", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Contract</button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Active Contracts",    val: s.activeContracts ?? contracts.filter((c) => c.status === "Active").length, accent: "text-teal-600" },
          { label: "Expiring Soon",       val: s.expiringSoon ?? contracts.filter((c) => c.displayStatus === "Expiring Soon").length, accent: "text-amber-600" },
          { label: "Expired",             val: s.expiredContracts ?? contracts.filter((c) => c.displayStatus === "Expired").length, accent: "text-red-600" },
          { label: "Customers Covered",   val: s.customersCovered ?? "--" },
          { label: "Renewal Queue",       val: s.renewalQueue ?? "--", accent: "text-amber-600" },
          { label: "Margin Risk",         val: s.marginRiskContracts ?? contracts.filter((c) => c.riskHeatScore === "High").length, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5 flex-wrap">
          {(["All", "Active", "Expiring Soon", "Expired", "Under Renewal"] as StatusFilter[]).map((f) => (
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
          title="Risk filter"
          value={riskFilter}
          onChange={(e) => setRiskFilter(e.target.value as typeof riskFilter)}
          className="border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
        >
          <option value="All">All Risk</option>
          <option value="High">High Risk</option>
          <option value="Medium">Medium Risk</option>
          <option value="Low">Low Risk</option>
        </select>
        <input
          type="search"
          placeholder="Search contracts, customers…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56"
        />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No contracts match your filters" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Contract", "Customer", "Rate Type", "Status", "Margin Risk", "Effective", "Expires", "Action", ""].map((h) => (
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
                      <p className="font-medium text-slate-900">{String(c.contractCode ?? "--")}</p>
                      <p className="text-xs text-slate-400 max-w-40 truncate">{String(c.title ?? "")}</p>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{String(c.customerName ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700 text-xs">{String(c.rateType ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(c.displayStatus ?? c.status ?? "Active")} /></td>
                    <td className="px-4 py-3"><RiskBadge level={String(c.riskHeatScore ?? "Low")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-600">{fmtDate(c.effectiveDate)}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{fmtDate(c.expiryDate)}</td>
                    <td className="px-4 py-3">
                      {c.recommendedAction ? (
                        <span className="text-xs text-slate-500 italic max-w-36 truncate block">{String(c.recommendedAction)}</span>
                      ) : null}
                    </td>
                    <td className="px-4 py-3">
                      {(String(c.displayStatus ?? c.status) === "Expired" || String(c.status) === "Draft") && (
                        <button
                          type="button"
                          className="text-xs px-2 py-1 rounded-lg bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 font-medium"
                          onClick={(e) => { e.stopPropagation(); activateMut.mutate(c.id as string | number); }}
                        >
                          Activate
                        </button>
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
              <span className="text-sm font-semibold text-white">{String(selected.contractCode)} — Detail</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6 flex gap-2 flex-wrap">
              <StatusBadge status={String(selected.displayStatus ?? selected.status ?? "Active")} />
              <RiskBadge level={String(selected.riskHeatScore ?? "Low")} />
            </div>
            <div className="px-5 py-4 border-b border-white/6">
              <p className="text-xs text-slate-400 mb-1">Title</p>
              <p className="text-sm font-semibold text-white">{String(selected.title ?? "")}</p>
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Customer", String(selected.customerName ?? "—")],
                ["Carrier", String(selected.carrierName ?? "—")],
                ["Rate Type", String(selected.rateType ?? "—")],
                ["Base Rate", selected.baseRate ? `$${Number(selected.baseRate).toFixed(2)}` : "—"],
                ["Effective", fmtDate(selected.effectiveDate)],
                ["Expires", fmtDate(selected.expiryDate)],
                ["Fuel Surcharge", selected.fuelSurchargeEnabled ? "Enabled" : "Disabled"],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            {!!selected.recommendedAction && (
              <div className="px-5 py-4">
                <p className="text-xs font-semibold text-amber-400 uppercase tracking-wide mb-1.5">Recommended Action</p>
                <p className="text-sm text-slate-300 leading-relaxed">{String(selected.recommendedAction)}</p>
              </div>
            )}
            <div className="px-5 py-4 border-t border-white/6">
              <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-2">Contract Versions</p>
              {(detail.versions as AnyRecord[] | undefined)?.length ? (
                <div className="space-y-2">
                  {(detail.versions as AnyRecord[]).slice(0, 4).map((version) => {
                    const isCurrent = Boolean(version.isCurrent ?? version.is_current);
                    return (
                      <div key={String(version.id)} className="rounded-lg border border-white/10 bg-white/5 p-3">
                        <div className="flex items-center justify-between gap-2">
                          <p className="text-sm font-semibold text-white">
                            Version {String(version.versionNo ?? version.version_no ?? "—")}
                          </p>
                          <span className="rounded-full border border-white/15 px-2 py-0.5 text-[11px] font-semibold text-slate-300">
                            {isCurrent ? "Current" : String(version.status ?? "draft")}
                          </span>
                        </div>
                        <p className="text-xs text-slate-300 mt-1">{String(version.versionLabel ?? version.version_label ?? "Snapshot")}</p>
                        <p className="text-xs text-slate-400 mt-1">{String(version.rateType ?? version.rate_type ?? "Per Mile")} · {String(version.marginRisk ?? version.margin_risk ?? "Low")} risk</p>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <p className="text-xs text-slate-400">No contract versions captured yet.</p>
              )}
            </div>
            {(String(selected.displayStatus ?? selected.status) === "Expired" || String(selected.status) === "Draft") && (
              <div className="px-5 py-4 border-t border-white/6 mt-auto">
                <button
                  type="button"
                  disabled={activateMut.isPending}
                  className="w-full text-sm px-3 py-2 rounded-lg bg-teal-600 hover:bg-teal-700 disabled:opacity-50 text-white font-medium transition-colors"
                  onClick={() => activateMut.mutate(selected.id as string | number)}
                >
                  {activateMut.isPending ? "Activating…" : "Activate Contract"}
                </button>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
