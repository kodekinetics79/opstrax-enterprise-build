import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, BadgeCheck, ShieldCheck, RefreshCw } from "lucide-react";
import { taxApi } from "@/services/taxApi";
import { ErrorState, LoadingState, PageHeader } from "@/components/ui";
import type { AnyRecord } from "@/types";

const REGIMES = ["vat", "gst", "zatca_vat", "us_sales_tax"];
const TAX_CODES = ["STANDARD", "REDUCED", "ZERO", "EXEMPT", "REVERSE_CHARGE", "OUT_OF_SCOPE"];
const CATEGORIES = ["S", "Z", "E", "O"];

// Tax configuration admin (ADR-008 P3). Manage VAT/GST/ZATCA tax profiles + decision-table rules,
// publish with maker-checker, and record the seller VAT registration required to charge tax.
export function TaxAdminPage() {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<number | null>(null);

  const profilesQ = useQuery({ queryKey: ["tax-profiles"], queryFn: () => taxApi.profiles() });
  const detailQ = useQuery({ queryKey: ["tax-profile", selected], queryFn: () => taxApi.profile(selected!), enabled: selected != null });

  const invalidate = () => { qc.invalidateQueries({ queryKey: ["tax-profiles"] }); if (selected != null) qc.invalidateQueries({ queryKey: ["tax-profile", selected] }); };

  const createProfile = useMutation({ mutationFn: (b: Record<string, unknown>) => taxApi.upsertProfile(b), onSuccess: invalidate });
  const addRule = useMutation({ mutationFn: (b: { id: number; body: Record<string, unknown> }) => taxApi.addRule(b.id, b.body), onSuccess: invalidate });
  const publish = useMutation({ mutationFn: (id: number) => taxApi.publish(id), onSuccess: invalidate });
  const saveSeller = useMutation({ mutationFn: (b: Record<string, unknown>) => taxApi.upsertSellerRegistration(b), onSuccess: () => {} });

  if (profilesQ.isLoading) return <LoadingState />;
  if (profilesQ.isError) return <ErrorState message="Could not load tax profiles." onRetry={() => { void profilesQ.refetch(); }} />;

  const profiles = (profilesQ.data as AnyRecord[]) ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Tax Configuration"
        description="VAT / GST / ZATCA profiles, decision-table rules and seller registration"
        actions={<button onClick={() => void profilesQ.refetch()} className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50"><RefreshCw className="h-4 w-4" /> Refresh</button>}
      />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Profiles list + create */}
        <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-slate-500">Tax profiles</h2>
          <div className="mb-4 overflow-hidden rounded-lg border border-slate-100">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs uppercase text-slate-400"><tr><th className="p-2">Name</th><th className="p-2">Regime</th><th className="p-2">Status</th><th className="p-2"></th></tr></thead>
              <tbody>
                {profiles.length === 0 ? <tr><td colSpan={4} className="p-3 text-slate-400">No profiles — absence of a published profile means invoices carry zero tax (today's behavior).</td></tr> :
                  profiles.map((p) => (
                    <tr key={String(p.id)} className={`border-t border-slate-100 ${selected === Number(p.id) ? "bg-blue-50" : ""}`}>
                      <td className="p-2 font-medium text-slate-700">{String(p.profileName ?? p.profileCode)}</td>
                      <td className="p-2">{String(p.regime)}</td>
                      <td className="p-2"><StatusPill status={String(p.status)} /></td>
                      <td className="p-2 text-right"><button onClick={() => setSelected(Number(p.id))} className="text-blue-600 hover:underline">manage</button></td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
          <ProfileForm onSubmit={(b) => createProfile.mutate(b)} busy={createProfile.isPending} />
        </section>

        {/* Selected profile: rules + publish */}
        <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-slate-500">Rules & publish</h2>
          {selected == null ? <p className="text-sm text-slate-400">Select a profile to manage its rules.</p> : detailQ.isLoading ? <LoadingState /> : (() => {
            const d = detailQ.data as AnyRecord | undefined;
            const status = String(d?.status ?? "draft");
            return (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <div><span className="font-medium text-slate-700">{String(d?.profileName ?? "")}</span> <StatusPill status={status} /></div>
                  {status === "draft" ? (
                    <button onClick={() => publish.mutate(selected)} disabled={publish.isPending}
                      className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50">
                      <BadgeCheck className="h-4 w-4" /> Publish
                    </button>
                  ) : null}
                </div>
                {publish.data ? <p className="text-xs text-amber-600">{String((publish.data as AnyRecord).message ?? (publish.data as AnyRecord).reason ?? "Publish requested — maker-checker: a different user must approve/publish.")}</p> : null}
                {status !== "draft" ? <p className="text-xs text-slate-400">Published profiles are immutable — create a new draft to change rates.</p> :
                  <RuleForm onSubmit={(b) => addRule.mutate({ id: selected, body: b })} busy={addRule.isPending} />}
              </div>
            );
          })()}
        </section>
      </div>

      {/* Seller registration */}
      <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-slate-500"><ShieldCheck className="h-4 w-4" /> Seller VAT registration</h2>
        <p className="mb-3 text-xs text-slate-400">Required before any non-zero VAT/ZATCA tax can be charged. Sources the ZATCA seller TRN.</p>
        <SellerForm onSubmit={(b) => saveSeller.mutate(b)} busy={saveSeller.isPending} saved={Boolean(saveSeller.data)} />
      </section>
    </div>
  );
}

function StatusPill({ status }: { status: string }) {
  const map: Record<string, string> = { published: "bg-emerald-100 text-emerald-700", draft: "bg-slate-100 text-slate-600", pending_approval: "bg-amber-100 text-amber-700", archived: "bg-slate-100 text-slate-400" };
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${map[status] ?? "bg-slate-100 text-slate-600"}`}>{status}</span>;
}

function Input(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className="w-full rounded-lg border border-slate-200 px-3 py-1.5 text-sm focus:border-blue-400 focus:outline-none" />;
}
function Select({ options, ...props }: { options: string[] } & React.SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className="w-full rounded-lg border border-slate-200 px-3 py-1.5 text-sm focus:border-blue-400 focus:outline-none">{options.map((o) => <option key={o} value={o}>{o}</option>)}</select>;
}
function SubmitBtn({ busy, label }: { busy: boolean; label: string }) {
  return <button type="submit" disabled={busy} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"><Plus className="h-4 w-4" /> {label}</button>;
}

function ProfileForm({ onSubmit, busy }: { onSubmit: (b: Record<string, unknown>) => void; busy: boolean }) {
  const [f, setF] = useState({ profileName: "", regime: "vat", currency: "", priceInclusive: "false", effectiveDate: "" });
  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit({ ...f }); setF({ profileName: "", regime: "vat", currency: "", priceInclusive: "false", effectiveDate: "" }); }} className="grid grid-cols-2 gap-2">
      <Input placeholder="Profile name" value={f.profileName} onChange={(e) => setF({ ...f, profileName: e.target.value })} required />
      <Select options={REGIMES} value={f.regime} onChange={(e) => setF({ ...f, regime: e.target.value })} />
      <Input placeholder="Currency (blank = any)" value={f.currency} onChange={(e) => setF({ ...f, currency: e.target.value })} />
      <Select options={["false", "true"]} value={f.priceInclusive} onChange={(e) => setF({ ...f, priceInclusive: e.target.value })} />
      <Input type="date" value={f.effectiveDate} onChange={(e) => setF({ ...f, effectiveDate: e.target.value })} required />
      <div className="col-span-2"><SubmitBtn busy={busy} label="Create draft profile" /></div>
    </form>
  );
}

function RuleForm({ onSubmit, busy }: { onSubmit: (b: Record<string, unknown>) => void; busy: boolean }) {
  const [f, setF] = useState({ taxCode: "STANDARD", taxCategory: "S", rate: "0.15", taxable: "true", matchChargeCode: "" });
  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit({ ...f }); }} className="grid grid-cols-2 gap-2 border-t border-slate-100 pt-3">
      <Select options={TAX_CODES} value={f.taxCode} onChange={(e) => setF({ ...f, taxCode: e.target.value })} />
      <Select options={CATEGORIES} value={f.taxCategory} onChange={(e) => setF({ ...f, taxCategory: e.target.value })} />
      <Input placeholder="Rate (e.g. 0.15)" value={f.rate} onChange={(e) => setF({ ...f, rate: e.target.value })} />
      <Select options={["true", "false"]} value={f.taxable} onChange={(e) => setF({ ...f, taxable: e.target.value })} />
      <Input placeholder="Match charge code (blank = catch-all)" value={f.matchChargeCode} onChange={(e) => setF({ ...f, matchChargeCode: e.target.value })} />
      <div><SubmitBtn busy={busy} label="Add rule" /></div>
    </form>
  );
}

function SellerForm({ onSubmit, busy, saved }: { onSubmit: (b: Record<string, unknown>) => void; busy: boolean; saved: boolean }) {
  const [f, setF] = useState({ jurisdiction: "SA", regime: "zatca_vat", taxRegistrationNo: "", legalName: "" });
  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit({ ...f }); }} className="grid grid-cols-2 gap-2 md:grid-cols-4">
      <Input placeholder="Jurisdiction" value={f.jurisdiction} onChange={(e) => setF({ ...f, jurisdiction: e.target.value })} />
      <Select options={["zatca_vat", "vat", "gst"]} value={f.regime} onChange={(e) => setF({ ...f, regime: e.target.value })} />
      <Input placeholder="Tax registration no (TRN)" value={f.taxRegistrationNo} onChange={(e) => setF({ ...f, taxRegistrationNo: e.target.value })} required />
      <Input placeholder="Legal name" value={f.legalName} onChange={(e) => setF({ ...f, legalName: e.target.value })} />
      <div className="col-span-2 md:col-span-4 flex items-center gap-3"><SubmitBtn busy={busy} label="Save registration" />{saved ? <span className="text-xs text-emerald-600">Saved</span> : null}</div>
    </form>
  );
}
