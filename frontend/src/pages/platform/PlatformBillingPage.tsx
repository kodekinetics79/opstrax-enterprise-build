import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PHeader, PCard, PKpi, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty, PDrawer } from "./ui";

export function PlatformBillingPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:billing:manage");
  const { data, isLoading, error } = useQuery({ queryKey: ["platform", "invoices"], queryFn: platformApi.invoices });
  const { data: tenants } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });
  const [createOpen, setCreateOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const rows = (data ?? []) as AnyRecord[];
  const sum = (pred: (r: AnyRecord) => boolean) => rows.filter(pred).reduce((acc, r) => acc + Number(r.amountCents || 0), 0);
  const collected = sum((r) => String(r.status) === "paid");
  const outstanding = sum((r) => ["sent", "overdue"].includes(String(r.status)));

  const markPaid = async (id: number) => {
    setBusy(true);
    try { await platformApi.markPaid(id); qc.invalidateQueries({ queryKey: ["platform", "invoices"] }); }
    finally { setBusy(false); }
  };

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Billing & Invoices"
        title="Collections"
        description="Create recurring and one-time invoices, track payment status, and manage collections."
        actions={canManage ? <PButton onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New Invoice</PButton> : undefined}
      />

      <div className="grid gap-4 sm:grid-cols-3">
        <PKpi label="Collected" value={formatMoney(collected)} tone="good" />
        <PKpi label="Outstanding" value={formatMoney(outstanding)} tone={outstanding > 0 ? "warn" : "default"} />
        <PKpi label="Invoices" value={rows.length} />
      </div>

      {rows.length === 0 ? (
        <PEmpty title="No invoices yet" subtitle="Create an invoice to begin tracking collections." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px] text-left text-sm">
              <thead className="border-b border-slate-800 bg-slate-900/80">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {["Invoice", "Tenant", "Amount", "Status", "Due", "Paid", ""].map((h) => <th key={h} className="px-5 py-3 font-semibold">{h}</th>)}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800">
                {rows.map((r) => (
                  <tr key={String(r.id)} className="hover:bg-slate-800/40">
                    <td className="px-5 py-3.5 font-mono text-xs text-slate-300">{String(r.invoiceNumber)}</td>
                    <td className="px-5 py-3.5 text-slate-200">{String(r.tenant)}</td>
                    <td className="px-5 py-3.5 font-semibold text-slate-100">{formatMoney(Number(r.amountCents), String(r.currency ?? "USD"))}</td>
                    <td className="px-5 py-3.5"><PBadge value={r.status} /></td>
                    <td className="px-5 py-3.5 font-mono text-xs text-slate-500">{String(r.dueAt ?? "").slice(0, 10)}</td>
                    <td className="px-5 py-3.5 font-mono text-xs text-slate-500">{String(r.paidAt ?? "").slice(0, 10) || "—"}</td>
                    <td className="px-5 py-3.5 text-right">
                      {canManage && String(r.status) !== "paid" && (
                        <PButton variant="ghost" disabled={busy} onClick={() => markPaid(Number(r.id))}>Mark paid</PButton>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PCard>
      )}

      {createOpen && (
        <CreateInvoiceDrawer
          tenants={(tenants ?? []) as AnyRecord[]}
          onClose={() => setCreateOpen(false)}
          onCreated={() => { setCreateOpen(false); qc.invalidateQueries({ queryKey: ["platform", "invoices"] }); }}
        />
      )}
    </div>
  );
}

function CreateInvoiceDrawer({ tenants, onClose, onCreated }: { tenants: AnyRecord[]; onClose: () => void; onCreated: () => void }) {
  const [form, setForm] = useState({ companyId: "", amount: "0", kind: "recurring", dueDays: "15", notes: "" });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      await platformApi.createInvoice({
        companyId: Number(form.companyId),
        amountCents: Math.round((Number(form.amount) || 0) * 100),
        kind: form.kind,
        dueDays: Number(form.dueDays),
        notes: form.notes || undefined,
        status: "sent",
      });
      onCreated();
    } catch (e) { setErr(e instanceof Error ? e.message : "Failed"); } finally { setBusy(false); }
  };

  return (
    <PDrawer open onClose={onClose} title="New Invoice">
      {err && <div className="mb-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">{err}</div>}
      <div className="space-y-4">
        <PField label="Tenant">
          <PSelect value={form.companyId} onChange={(e) => setForm({ ...form, companyId: e.target.value })}>
            <option value="">— Select tenant —</option>
            {tenants.map((t) => <option key={String(t.id)} value={String(t.id)}>{String(t.name)}</option>)}
          </PSelect>
        </PField>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Amount ($)"><PInput type="number" value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} /></PField>
          <PField label="Due in (days)"><PInput type="number" value={form.dueDays} onChange={(e) => setForm({ ...form, dueDays: e.target.value })} /></PField>
        </div>
        <PField label="Type">
          <PSelect value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })}>
            <option value="recurring">Recurring</option>
            <option value="one_time">One-time</option>
          </PSelect>
        </PField>
        <PField label="Notes"><PInput value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} /></PField>
        <div className="flex gap-2 pt-2">
          <PButton onClick={submit} disabled={busy || !form.companyId}>Create invoice</PButton>
          <PButton variant="ghost" onClick={onClose}>Cancel</PButton>
        </div>
      </div>
    </PDrawer>
  );
}
