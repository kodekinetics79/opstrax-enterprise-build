import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { useTenantCurrency } from "@/hooks/useTenantRegion";
import type { AnyRecord } from "@/types";

const rateCardsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/rate-cards")).then((rows) =>
    rows.map((r) => ({
      ...r,
      rateCardId: r.rateCardId ?? r.code ?? `RC-${String(r.id)}`,
      rateCardName: r.rateCardName ?? r.title ?? "",
      originZone: r.originZone ?? r.origin ?? r.locationName ?? "",
      destinationZone: r.destinationZone ?? r.destination ?? "",
      baseRate: r.baseRate != null || r.amount != null ? Number(r.baseRate ?? r.amount) : null,
      fuelSurchargePercent: r.fuelSurchargePercent != null ? Number(r.fuelSurchargePercent) : null,
      effectiveFrom: r.effectiveDate ?? r.effectiveFrom ?? "",
      effectiveTo: r.expiryDate ?? r.effectiveTo ?? r.dueAt ?? "",
      pricingMethod: r.billingBasis ?? r.pricingMethod ?? "",
    }))
  ),
  create: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/rate-cards", body)),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Active" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Expiring Soon" ? "bg-amber-50 border-amber-200 text-amber-700" :
    status === "Expired" ? "bg-red-50 border-red-200 text-red-700" :
    "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function VehicleTypeBadge({ type }: { type: string }) {
  if (!type) return <span className="text-xs text-slate-500">—</span>;
  const cls =
    type.includes("Reefer") ? "bg-blue-50 border-blue-200 text-blue-700" :
    type.includes("Last-mile") || type.includes("Van") ? "bg-violet-50 border-violet-200 text-violet-700" :
    type.includes("Flatbed") ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{type}</span>;
}

// ── Create Rate Card Modal ────────────────────────────────────────────────────

function CreateRateCardModal({ onClose, onSaved, defaultCurrency }: { onClose: () => void; onSaved: () => void; defaultCurrency: string }) {
  const [form, setForm] = useState({ title: "", originZone: "", destinationZone: "", vehicleType: "Dry Van", pricingMethod: "Per KM", baseRate: "", fuelSurcharge: "", currency: defaultCurrency, effectiveDate: "" });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => rateCardsApi.create({
      rateCardName: form.title,
      originZone: form.originZone,
      destinationZone: form.destinationZone,
      vehicleType: form.vehicleType,
      billingBasis: form.pricingMethod,
      baseRate: form.baseRate,
      fuelSurchargePercent: form.fuelSurcharge || null,
      currency: form.currency,
      effectiveDate: form.effectiveDate,
      status: "Active",
    } as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["rate-cards"] }); onSaved(); },
  });
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-lg p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">New Rate Card</h2>
        <div className="grid grid-cols-2 gap-3">
          <div className="col-span-2">
            <label className="block text-xs font-medium text-slate-600 mb-1">Rate Card Name*</label>
            <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
              placeholder="Riyadh to Dammam standard" value={form.title} onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))} />
          </div>
          {[
            { label: "Origin Zone", key: "originZone", placeholder: "Riyadh DC" },
            { label: "Destination Zone", key: "destinationZone", placeholder: "Dammam Retail" },
            { label: "Rate / Base Amount", key: "baseRate", placeholder: "5.8" },
          ].map(({ label, key, placeholder }) => (
            <div key={key}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
                placeholder={placeholder} value={String(form[key as keyof typeof form])}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))} />
            </div>
          ))}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Vehicle Type</label>
            <select title="Vehicle Type" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.vehicleType} onChange={(e) => setForm((f) => ({ ...f, vehicleType: e.target.value }))}>
              {["Dry Van", "Reefer", "Last-mile Van", "Flatbed", "Box Truck"].map((t) => <option key={t}>{t}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Pricing Method</label>
            <select title="Pricing Method" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.pricingMethod} onChange={(e) => setForm((f) => ({ ...f, pricingMethod: e.target.value }))}>
              {["Per KM", "Fixed Trip", "Zone Based", "Per Pallet", "Weight Based"].map((m) => <option key={m}>{m}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Fuel Surcharge</label>
            <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
              placeholder="Enter approved percentage" value={form.fuelSurcharge} onChange={(e) => setForm((f) => ({ ...f, fuelSurcharge: e.target.value }))} />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Currency</label>
            <select title="Currency" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.currency} onChange={(e) => setForm((f) => ({ ...f, currency: e.target.value }))}>
              <option value="">Select currency</option>
              {["USD", "CAD", "SAR", "AED", "PKR", "EUR", "GBP"].map((c) => <option key={c}>{c}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Effective Date*</label>
            <input type="date" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.effectiveDate} onChange={(e) => setForm((f) => ({ ...f, effectiveDate: e.target.value }))} />
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" disabled={!form.title || !form.currency || !form.effectiveDate || mut.isPending} className="btn-primary text-sm" onClick={() => mut.mutate()}>
            {mut.isPending ? "Saving…" : "Create Rate Card"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function RateCardsPage() {
  const tenantCurrency = useTenantCurrency() ?? "";
  const [statusFilter, setStatusFilter] = useState<"All" | "Active" | "Expiring Soon" | "Expired">("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["rate-cards", "list"], queryFn: rateCardsApi.list });
  const cards = (listQ.data ?? []) as AnyRecord[];

  const active = cards.filter((c) => c.status === "Active").length;
  const expiring = cards.filter((c) => c.status === "Expiring Soon").length;

  const filtered = cards.filter((c) => {
    if (statusFilter !== "All" && c.status !== statusFilter) return false;
    if (search) {
      const sq = search.toLowerCase();
      return String(c.rateCardName ?? "").toLowerCase().includes(sq) ||
             String(c.originZone ?? "").toLowerCase().includes(sq) ||
             String(c.vehicleType ?? "").toLowerCase().includes(sq);
    }
    return true;
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      {showCreate && <CreateRateCardModal defaultCurrency={tenantCurrency} onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Rate Cards</h1>
          <p className="text-sm text-slate-500 mt-0.5">Persisted lane rates, pricing basis, fuel surcharge and effective periods</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("rate-cards", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Rate Card</button>
        </div>
      </div>

      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Rate Cards", val: cards.length },
          { label: "Active", val: active, accent: "text-teal-600" },
          { label: "Expiring Soon", val: expiring, accent: "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Active", "Expiring Soon", "Expired"] as const).map((f) => (
            <button key={f} type="button" onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>{f}</button>
          ))}
        </div>
        <input type="search" placeholder="Search rate card, zone, vehicle…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52" />
      </div>

      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No rate cards match your filters" /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Rate Card", "Name", "Lane", "Vehicle", "Method", "Rate", "Fuel Surch.", "Effective", "Expires", "Status"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((c, i) => (
                  <tr key={String(c.id ?? i)} className={`hover:bg-slate-50 cursor-pointer ${selected?.id === c.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === c.id ? null : c)}>
                    <td className="px-4 py-3 font-medium text-slate-900">{String(c.rateCardId ?? "--")}</td>
                    <td className="px-4 py-3 text-xs text-slate-700 max-w-36 truncate">{String(c.rateCardName ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(c.originZone ?? "—")} → {String(c.destinationZone ?? "—")}</td>
                    <td className="px-4 py-3"><VehicleTypeBadge type={String(c.vehicleType ?? "")} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(c.pricingMethod ?? "—")}</td>
                    <td className="px-4 py-3 font-medium text-slate-900">{c.baseRate == null || !c.currency ? "—" : `${String(c.currency)} ${Number(c.baseRate).toLocaleString()}`}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{c.fuelSurchargePercent == null ? "—" : `${Number(c.fuelSurchargePercent).toLocaleString()}%`}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(c.effectiveFrom ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(c.effectiveTo ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(c.status ?? "Active")} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selected && (
        <div className="fixed inset-0 z-40 flex justify-end" onClick={() => setSelected(null)}>
          <div className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
              <span className="text-sm font-semibold text-white">{String(selected.rateCardId)}</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6 flex gap-2">
              <StatusBadge status={String(selected.status ?? "Active")} />
              <VehicleTypeBadge type={String(selected.vehicleType ?? "")} />
            </div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Rate Card Name", String(selected.rateCardName ?? "—")],
                ["Contract ID", String(selected.contractId ?? "—")],
                ["Origin", String(selected.originZone ?? "—")],
                ["Destination", String(selected.destinationZone ?? "—")],
                ["Pricing Method", String(selected.pricingMethod ?? "—")],
                ["Rate", selected.baseRate == null || !selected.currency ? "—" : `${String(selected.currency)} ${Number(selected.baseRate).toLocaleString()}`],
                ["Fuel Surcharge", selected.fuelSurchargePercent == null ? "—" : `${Number(selected.fuelSurchargePercent).toLocaleString()}%`],
                ["Effective", String(selected.effectiveFrom ?? "—")],
                ["Expires", String(selected.effectiveTo ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Rate Health</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {String(selected.status) === "Expiring Soon"
                  ? "Rate card expires soon. Initiate renewal discussion with customer before expiry to avoid service disruption."
                  : String(selected.status) === "Expired"
                  ? "Rate card expired. New rates required before booking additional loads on this lane."
                  : "Rate card is active. Monitor fuel cost vs surcharge recovery to maintain margin."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
