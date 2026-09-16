import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { contractsApi } from "@/services/contractsApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { CommercialDisclosure, CommercialMetricRail, CommercialToolbar, RevenueWorkspaceHeader } from "@/components/CommercialWorkspace";
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

function fmtDate(d: unknown): string {
  if (!d) return "—";
  try { return new Date(String(d)).toLocaleDateString("en-GB", { timeZone: "UTC", day: "2-digit", month: "short", year: "numeric" }); }
  catch { return String(d); }
}

// ── Create Contract Modal ────────────────────────────────────────────────────

function CreateContractModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ contractNumber: "", title: "", customerId: "", rateType: "FTL", effectiveDate: "", expiryDate: "" });
  const [customerSearch, setCustomerSearch] = useState("");
  const customerQ = useQuery({ queryKey: ["contracts", "customer-options", customerSearch], queryFn: () => contractsApi.customerOptions(customerSearch) });
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
            { label: "Contract Number*", key: "contractNumber", placeholder: "CON-2001" },
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
          <div className="col-span-2">
            <label htmlFor="contract-customer-search" className="block text-xs font-medium text-slate-600 mb-1">Find customer</label>
            <input id="contract-customer-search" className="field mb-2" type="search" placeholder="Search customer name or code" value={customerSearch} onChange={(e) => { setCustomerSearch(e.target.value); setForm((f) => ({ ...f, customerId: "" })); }} />
            <label htmlFor="contract-customer" className="block text-xs font-medium text-slate-600 mb-1">Customer*</label>
            <select id="contract-customer" className="field" required value={form.customerId} disabled={customerQ.isPending || customerQ.isError} onChange={(e) => setForm((f) => ({ ...f, customerId: e.target.value }))}>
              <option value="">{customerQ.isPending ? "Loading customers..." : "Select an active customer"}</option>
              {(customerQ.data ?? []).map((customer) => <option key={String(customer.id)} value={String(customer.id)}>{String(customer.customerCode)} - {String(customer.name)}</option>)}
            </select>
            {customerQ.isError && <p role="alert" className="text-sm text-red-600">Could not load customers. <button type="button" onClick={() => void customerQ.refetch()}>Retry</button></p>}
            {!customerQ.isPending && !customerQ.isError && !customerQ.data?.length && <p className="text-xs text-slate-500">No active customers match. Create an active customer or change your search.</p>}
          </div>
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
            disabled={!form.contractNumber || !form.title || !form.customerId || customerQ.isPending || customerQ.isError || !(customerQ.data ?? []).some((customer) => String(customer.id) === form.customerId) || mut.isPending}
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
    <div className="page-stack min-w-0">
      {showCreate && (
        <CreateContractModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />
      )}

      <RevenueWorkspaceHeader
        title="Contracts"
        description="Govern commercial terms, effective dates, renewal exposure and rate ownership."
        activeStage="contracts"
        actions={<div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("contracts", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Contract</button>
        </div>}
      />

      <CommercialMetricRail metrics={[
        { label: "All contracts", value: contracts.length, detail: `${filtered.length} shown`, active: statusFilter === "All", onClick: () => setStatusFilter("All") },
        { label: "Active", value: String(s.activeContracts ?? contracts.filter((c) => c.status === "Active").length), detail: `${String(s.customersCovered ?? "—")} customers`, tone: "good", active: statusFilter === "Active", onClick: () => setStatusFilter("Active") },
        { label: "Expiring soon", value: String(s.expiringSoon ?? contracts.filter((c) => c.displayStatus === "Expiring Soon").length), detail: "renewal attention", tone: "warn", active: statusFilter === "Expiring Soon", onClick: () => setStatusFilter("Expiring Soon") },
        { label: "Expired", value: String(s.expiredContracts ?? contracts.filter((c) => c.displayStatus === "Expired").length), detail: "cannot govern jobs", tone: "bad", active: statusFilter === "Expired", onClick: () => setStatusFilter("Expired") },
        { label: "Renewal queue", value: String(s.renewalQueue ?? "—"), detail: "recorded actions", tone: "warn", active: statusFilter === "Under Renewal", onClick: () => setStatusFilter("Under Renewal") },
        { label: "Origin unverified", value: String(s.legacyOriginUnverified ?? "—"), detail: "needs provenance", tone: "warn" },
      ]} />

      {/* Filters */}
      <CommercialToolbar
        filters={<>
          {(["All", "Active", "Expiring Soon", "Expired", "Under Renewal"] as StatusFilter[]).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`filter-chip shrink-0 ${
                statusFilter === f
                  ? "filter-chip-active"
                  : ""
              }`}
            >
              {f}
            </button>
          ))}
        </>}
        meta={`${filtered.length} of ${contracts.length}`}
        search={<input
          type="search"
          aria-label="Search contracts"
          placeholder="Search contracts, customers…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="field"
        />}
      />

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No contracts match your filters" subtitle={contracts.length ? "Clear a status or search filter to see the full contract register." : "Create the first contract after customer, dates and rate ownership are confirmed."} action={!contracts.length ? <button type="button" className="btn-primary" onClick={() => setShowCreate(true)}>New Contract</button> : undefined} />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Contract", "Origin", "Customer", "Rate Type", "Status", "Effective", "Expires", "Action", ""].map((h) => (
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
                    <td className="px-4 py-3 text-xs text-slate-600">{String(c.recordOrigin ?? "Legacy origin unverified")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(c.customerName ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700 text-xs">{String(c.rateType ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(c.displayStatus ?? c.status ?? "Active")} /></td>
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

      <CommercialDisclosure>
        <p><strong>Contract evidence:</strong> Status, effective dates, rates and version history come from persisted tenant-scoped records.</p>
        <p><strong>Activation:</strong> Activate only reviewed drafts or expired records with confirmed customer and rate terms. Origin-unverified records remain visibly flagged.</p>
      </CommercialDisclosure>

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
              <span className="inline-flex text-xs px-2 py-0.5 rounded-full border border-white/15 text-slate-300">{String(selected.recordOrigin ?? "Legacy origin unverified")}</span>
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
                ["Base Rate", selected.baseRate ? `${Number(selected.baseRate).toFixed(4)} ${String(selected.currency ?? "")}` : "—"],
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
                        <p className="text-xs text-slate-400 mt-1">{String(version.rateType ?? version.rate_type ?? "Per Mile")}</p>
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
