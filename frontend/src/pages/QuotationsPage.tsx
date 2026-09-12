import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { requireCommercialModuleRecords } from "@/services/commercialModulePayload";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { useTenantCurrency } from "@/hooks/useTenantRegion";
import type { AnyRecord } from "@/types";

const quotationsApi = {
  list: () =>
    unwrap<unknown>(apiClient.get("/api/quotations")).then((payload) =>
      requireCommercialModuleRecords(payload, "quotations").map((r) => ({
        ...r,
        quoteId: r.quoteId ?? r.code ?? `QT-${String(r.id)}`,
        customer: r.customer ?? r.title ?? "",
        origin: r.origin ?? r.locationName ?? "",
        destination: r.destination ?? "",
        quoteAmount: r.quoteAmount != null || r.amount != null ? Number(r.quoteAmount ?? r.amount) : null,
        currency: r.currency ?? "",
        margin: r.margin ?? "—",
        marginPct: r.margin != null && Number.isFinite(parseFloat(String(r.margin).replace("%", "")))
          ? parseFloat(String(r.margin).replace("%", ""))
          : null,
        validUntil: r.validUntil ?? r.dueAt ?? "",
      }))
    ),
  create: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/quotations", body)),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Accepted" ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Sent" ? "bg-blue-50 border-blue-200 text-blue-700" :
    status === "Draft" ? "bg-slate-100 border-slate-200 text-slate-600" :
    status === "Expired" ? "bg-red-50 border-red-200 text-red-700" :
    "bg-amber-50 border-amber-200 text-amber-700";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

function MarginBar({ pct }: { pct: number | null }) {
  if (pct == null) return <span className="text-xs text-slate-500">Not calculated</span>;
  const color = pct >= 25 ? "bg-teal-500" : pct >= 15 ? "bg-amber-400" : "bg-red-400";
  return (
    <div className="flex items-center gap-2 text-xs">
      <div className="w-16 h-1.5 rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${Math.min(100, pct * 2)}%` }} />
      </div>
      <span className="font-medium text-slate-700">{pct.toFixed(0)}%</span>
    </div>
  );
}

// ── Create Quote Modal ────────────────────────────────────────────────────────

function CreateQuoteModal({ onClose, onSaved, defaultCurrency }: { onClose: () => void; onSaved: () => void; defaultCurrency: string }) {
  const [form, setForm] = useState({ title: "", origin: "", destination: "", cargo: "", quoteAmount: "", currency: defaultCurrency, margin: "", validUntil: "" });
  const qc = useQueryClient();
  const mut = useMutation({
    mutationFn: () => quotationsApi.create({ ...form, status: "Draft", amount: form.quoteAmount } as unknown as AnyRecord),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["quotations"] }); onSaved(); },
  });
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div className="panel w-full max-w-lg p-6 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-base font-bold text-slate-900">New Quotation</h2>
        <div className="grid grid-cols-2 gap-3">
          <div className="col-span-2">
            <label className="block text-xs font-medium text-slate-600 mb-1">Customer / Lead*</label>
            <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
              placeholder="Jeddah Fresh Foods" value={form.title} onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))} />
          </div>
          {[
            { label: "Origin", key: "origin", placeholder: "Jeddah" },
            { label: "Destination", key: "destination", placeholder: "Riyadh" },
            { label: "Cargo Description", key: "cargo", placeholder: "Fresh produce" },
            { label: "Quote Amount", key: "quoteAmount", placeholder: "3900" },
            { label: "Margin (%)", key: "margin", placeholder: "18" },
            { label: "Valid Until", key: "validUntil", placeholder: "2026-09-30", type: "date" },
          ].map(({ label, key, placeholder, type }) => (
            <div key={key}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input type={type ?? "text"} className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
                placeholder={placeholder} value={String(form[key as keyof typeof form])}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))} />
            </div>
          ))}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Currency</label>
            <select title="Currency" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.currency} onChange={(e) => setForm((f) => ({ ...f, currency: e.target.value }))}>
              <option value="">Select currency</option>
              {["USD", "CAD", "SAR", "AED", "PKR", "EUR", "GBP"].map((c) => <option key={c}>{c}</option>)}
            </select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" disabled={!form.title || !form.currency || mut.isPending} className="btn-primary text-sm" onClick={() => mut.mutate()}>
            {mut.isPending ? "Saving…" : "Create Quote"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function QuotationsPage() {
  const tenantCurrency = useTenantCurrency() ?? "";
  const [statusFilter, setStatusFilter] = useState<"All" | "Draft" | "Sent" | "Accepted" | "Expired">("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["quotations", "list"], queryFn: quotationsApi.list });
  const quotes = (listQ.data ?? []) as AnyRecord[];

  const sent = quotes.filter((q) => q.status === "Sent").length;
  const accepted = quotes.filter((q) => q.status === "Accepted").length;
  const valuedQuotes = quotes.filter((q) => q.quoteAmount != null && Number.isFinite(Number(q.quoteAmount)) && q.currency);
  const quoteCurrencies = [...new Set(valuedQuotes.map((q) => String(q.currency).trim()).filter(Boolean))];
  const totalValue = valuedQuotes.reduce((s, q) => s + Number(q.quoteAmount), 0);
  const totalValueLabel = quoteCurrencies.length === 1
    ? `${quoteCurrencies[0]} ${totalValue.toLocaleString()}`
    : quoteCurrencies.length > 1 ? "Multiple currencies" : "—";
  const marginedQuotes = quotes.filter((q) => q.marginPct != null && Number.isFinite(Number(q.marginPct)));
  const avgMargin = marginedQuotes.length
    ? marginedQuotes.reduce((s, q) => s + Number(q.marginPct), 0) / marginedQuotes.length
    : null;

  const filtered = quotes.filter((q) => {
    if (statusFilter !== "All" && q.status !== statusFilter) return false;
    if (search) {
      const sq = search.toLowerCase();
      return String(q.customer ?? q.title ?? "").toLowerCase().includes(sq) ||
             String(q.origin ?? "").toLowerCase().includes(sq) ||
             String(q.destination ?? "").toLowerCase().includes(sq);
    }
    return true;
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) {
    return (
      <ErrorState
        message={(listQ.error as Error)?.message ?? "Unable to load live quotations."}
      />
    );
  }

  return (
    <div className="page-stack min-w-0">
      {showCreate && <CreateQuoteModal defaultCurrency={tenantCurrency} onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Quotations</h1>
          <p className="text-sm text-slate-500 mt-0.5">Price quotes — track draft, sent, accepted, and expired quotes with margin visibility</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("quotations", filtered)}>Export CSV</button>
          <button type="button" className="btn-primary text-sm" onClick={() => setShowCreate(true)}>New Quote</button>
        </div>
      </div>

      <div className="panel grid gap-3 md:grid-cols-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Quote integrity</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">This view reflects persisted quote records returned by the service.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Conversion boundary</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Automated quote-to-contract or booking conversion is not available in this build.</p>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Margin discipline</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">Every quote keeps route, cargo and margin context visible for review.</p>
        </div>
      </div>

      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Quotes",   val: quotes.length },
          { label: "Sent",           val: sent, accent: "text-blue-600" },
          { label: "Accepted",       val: accepted, accent: "text-teal-600" },
          { label: "Total Value",    val: totalValueLabel, accent: "text-violet-600" },
          { label: "Avg Margin",     val: avgMargin == null ? "—" : `${avgMargin.toFixed(1)}%`, accent: avgMargin != null && avgMargin >= 20 ? "text-teal-600" : "text-amber-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-32">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Draft", "Sent", "Accepted", "Expired"] as const).map((f) => (
            <button key={f} type="button" onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>{f}</button>
          ))}
        </div>
        <input type="search" placeholder="Search customer, route…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-52" />
      </div>

      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? <EmptyState title="No quotes match your filters" /> : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Quote", "Customer", "Route", "Cargo", "Amount", "Margin", "Valid Until", "Status"].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((q, i) => (
                  <tr key={String(q.id ?? i)} className={`hover:bg-slate-50 cursor-pointer ${selected?.id === q.id ? "bg-teal-50" : ""}`}
                    onClick={() => setSelected(selected?.id === q.id ? null : q)}>
                    <td className="px-4 py-3 font-medium text-slate-900">{String(q.quoteId ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(q.customer ?? q.title ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">{String(q.origin ?? "—")} → {String(q.destination ?? "—")}</td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(q.cargo ?? "—")}</td>
                    <td className="px-4 py-3 font-medium text-slate-900">{q.quoteAmount == null || !q.currency ? "—" : `${String(q.currency)} ${Number(q.quoteAmount).toLocaleString()}`}</td>
                    <td className="px-4 py-3"><MarginBar pct={q.marginPct == null ? null : Number(q.marginPct)} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(q.validUntil ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(q.status ?? "Draft")} /></td>
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
              <span className="text-sm font-semibold text-white">{String(selected.quoteId)}</span>
              <button type="button" className="text-slate-400 hover:text-white" aria-label="Close" onClick={() => setSelected(null)}>✕</button>
            </div>
            <div className="px-5 py-4 border-b border-white/6"><StatusBadge status={String(selected.status ?? "Draft")} /></div>
            <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
              {[
                ["Customer", String(selected.customer ?? selected.title ?? "—")],
                ["Cargo", String(selected.cargo ?? "—")],
                ["Origin", String(selected.origin ?? "—")],
                ["Destination", String(selected.destination ?? "—")],
                ["Amount", selected.quoteAmount == null || !selected.currency ? "—" : `${String(selected.currency)} ${Number(selected.quoteAmount).toLocaleString()}`],
                ["Margin", String(selected.margin ?? "—")],
                ["Valid Until", String(selected.validUntil ?? "—")],
              ].map(([k, v]) => (
                <div key={String(k)}>
                  <p className="text-xs text-slate-400">{String(k)}</p>
                  <p className="text-sm font-semibold text-white mt-0.5">{String(v)}</p>
                </div>
              ))}
            </div>
            <div className="px-5 py-4">
              <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">Pricing Insight</p>
              <p className="text-sm text-slate-300 leading-relaxed">
                {selected.marginPct == null ? "No persisted margin calculation is available for this quote." :
                 Number(selected.marginPct) >= 25 ? "Strong recorded margin. Review the persisted cost inputs before accepting." :
                 Number(selected.marginPct) >= 15 ? "Recorded margin is within the configured review band. Confirm fuel and accessorial inputs." :
                 "Recorded margin is below the display threshold. Review persisted cost inputs before accepting."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
