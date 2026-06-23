import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { quotations as seedQuotations } from "@/data/mockOperatingData";
import type { AnyRecord } from "@/types";

// ── Seed ────────────────────────────────────────────────────────────────────

function buildSeed(): AnyRecord[] {
  return (seedQuotations as AnyRecord[]).map((q, i) => ({
    id: i + 1,
    title: String(q.customer ?? ""),
    quoteId: String(q.quoteId ?? `QT-${5000 + i}`),
    customer: String(q.customer ?? ""),
    origin: String(q.origin ?? ""),
    destination: String(q.destination ?? ""),
    cargo: String(q.cargo ?? ""),
    quoteAmount: Number(q.quoteAmount ?? 0),
    currency: String(q.currency ?? "SAR"),
    margin: String(q.margin ?? "20%"),
    marginPct: parseFloat(String(q.margin ?? "20").replace("%", "")),
    validUntil: String(q.validUntil ?? ""),
    status: String(q.status ?? "Draft"),
  }));
}

const quotationsApi = {
  list: () => withFallback(
    unwrap<AnyRecord[]>(apiClient.get("/api/quotations")).then((rows) =>
      rows.map((r) => ({
        ...r,
        quoteId: r.quoteId ?? r.code ?? `QT-${String(r.id)}`,
        customer: r.customer ?? r.title ?? "",
        origin: r.origin ?? r.location_name ?? "",
        destination: r.destination ?? "",
        quoteAmount: Number(r.quoteAmount ?? r.amount ?? 0),
        currency: r.currency ?? "SAR",
        margin: r.margin ?? "—",
        marginPct: parseFloat(String(r.margin ?? "0").replace("%", "")) || 0,
        validUntil: r.validUntil ?? r.due_at ?? "",
      }))
    ),
    () => buildSeed()
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

function MarginBar({ pct }: { pct: number }) {
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

function CreateQuoteModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ title: "", origin: "", destination: "", cargo: "", quoteAmount: "", currency: "SAR" });
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
          ].map(({ label, key, placeholder }) => (
            <div key={key}>
              <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
              <input className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400"
                placeholder={placeholder} value={String(form[key as keyof typeof form])}
                onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.value }))} />
            </div>
          ))}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Currency</label>
            <select title="Currency" className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400"
              value={form.currency} onChange={(e) => setForm((f) => ({ ...f, currency: e.target.value }))}>
              {["SAR", "AED", "USD", "EUR"].map((c) => <option key={c}>{c}</option>)}
            </select>
          </div>
        </div>
        {mut.isError && <p className="text-xs text-red-600">{(mut.error as Error)?.message}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="button" disabled={!form.title || mut.isPending} className="btn-primary text-sm" onClick={() => mut.mutate()}>
            {mut.isPending ? "Saving…" : "Create Quote"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function QuotationsPage() {
  const [statusFilter, setStatusFilter] = useState<"All" | "Draft" | "Sent" | "Accepted" | "Expired">("All");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const listQ = useQuery({ queryKey: ["quotations", "list"], queryFn: quotationsApi.list });
  const quotes = (listQ.data ?? []) as AnyRecord[];
  const qc = useQueryClient();

  const sent = quotes.filter((q) => q.status === "Sent").length;
  const accepted = quotes.filter((q) => q.status === "Accepted").length;
  const totalValue = quotes.reduce((s, q) => s + Number(q.quoteAmount ?? 0), 0);
  const avgMargin = quotes.length ? quotes.reduce((s, q) => s + Number(q.marginPct ?? 0), 0) / quotes.length : 0;

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
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {showCreate && <CreateQuoteModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />}
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

      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Quotes",   val: quotes.length },
          { label: "Sent",           val: sent, accent: "text-blue-600" },
          { label: "Accepted",       val: accepted, accent: "text-teal-600" },
          { label: "Total Value",    val: `SAR ${totalValue.toLocaleString()}`, accent: "text-violet-600" },
          { label: "Avg Margin",     val: `${avgMargin.toFixed(1)}%`, accent: avgMargin >= 20 ? "text-teal-600" : "text-amber-600" },
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
                statusFilter === f ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
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
                  {["Quote", "Customer", "Route", "Cargo", "Amount", "Margin", "Valid Until", "Status", ""].map((h) => (
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
                    <td className="px-4 py-3 font-medium text-slate-900">{String(q.currency ?? "SAR")} {Number(q.quoteAmount ?? 0).toLocaleString()}</td>
                    <td className="px-4 py-3"><MarginBar pct={Number(q.marginPct ?? 0)} /></td>
                    <td className="px-4 py-3 text-xs text-slate-500">{String(q.validUntil ?? "—")}</td>
                    <td className="px-4 py-3"><StatusBadge status={String(q.status ?? "Draft")} /></td>
                    <td className="px-4 py-3">
                      {String(q.status) === "Accepted" && (
                        <button type="button" className="text-xs px-2 py-1 rounded-lg bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 font-medium whitespace-nowrap"
                          onClick={(e) => { e.stopPropagation(); void qc.invalidateQueries({ queryKey: ["contracts"] }); }}>
                          → Contract
                        </button>
                      )}
                      {String(q.status) === "Draft" && (
                        <button type="button" className="text-xs px-2 py-1 rounded-lg bg-blue-50 border border-blue-200 text-blue-700 hover:bg-blue-100 font-medium"
                          onClick={(e) => { e.stopPropagation(); void qc.invalidateQueries({ queryKey: ["quotations"] }); }}>
                          Send
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
                ["Amount", `${String(selected.currency ?? "SAR")} ${Number(selected.quoteAmount ?? 0).toLocaleString()}`],
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
                {Number(selected.marginPct ?? 0) >= 25 ? "Strong margin. This quote is competitively priced and profitable." :
                 Number(selected.marginPct ?? 0) >= 15 ? "Acceptable margin. Consider negotiating fuel surcharge inclusion." :
                 "Margin below target. Review cost inputs before accepting."}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
