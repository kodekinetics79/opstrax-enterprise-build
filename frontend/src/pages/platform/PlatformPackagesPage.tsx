import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PHeader, PCard, PBadge, PButton, PField, PInput, PLoading, PError, PEmpty, PDrawer, PConfirm } from "./ui";

const MODULE_OPTIONS = [
  "safety", "maintenance", "dispatch", "telematics", "crm", "customer_portal", "reports", "compliance",
];

function parseModules(v: unknown): string[] {
  try { return Array.isArray(v) ? (v as string[]) : JSON.parse(String(v ?? "[]")); } catch { return []; }
}

export function PlatformPackagesPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:packages:manage");
  const { data, isLoading, error } = useQuery({ queryKey: ["platform", "packages"], queryFn: platformApi.packages });

  const [createOpen, setCreateOpen] = useState(false);
  const [editPkg, setEditPkg] = useState<AnyRecord | null>(null);
  const [deletePkg, setDeletePkg] = useState<AnyRecord | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);

  const refresh = () => qc.invalidateQueries({ queryKey: ["platform", "packages"] });

  const run = async (fn: () => Promise<unknown>, done: string) => {
    setBusy(true); setNotice(null);
    try { await fn(); refresh(); setNotice({ ok: true, text: done }); }
    catch (e) { setNotice({ ok: false, text: e instanceof Error ? e.message : "Action failed" }); }
    finally { setBusy(false); setDeletePkg(null); }
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const rows = (data ?? []) as AnyRecord[];

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Packages & Pricing"
        title="Pricing packages"
        description="Define base, per-seat, setup and annual pricing with bundled module access. Create, edit, activate or delete."
        actions={canManage ? <PButton onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New Package</PButton> : undefined}
      />

      {notice && (
        <div className={`rounded-xl border px-4 py-2.5 text-sm ${notice.ok ? "border-teal-500/30 bg-teal-500/5 text-teal-700" : "border-red-300 bg-red-50 text-red-700"}`}>
          {notice.text}
        </div>
      )}

      {rows.length === 0 ? (
        <PEmpty title="No packages yet" subtitle="Create a package to standardise pricing and module bundles." />
      ) : (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {rows.map((p) => {
            const modules = parseModules(p.moduleKeys);
            return (
              <PCard key={String(p.id)} className="flex flex-col p-5">
                <div className="flex items-start justify-between">
                  <div>
                    <h3 className="text-lg font-bold text-white">{String(p.name)}</h3>
                    <p className="text-xs text-slate-500">{String(p.packageCode)}</p>
                  </div>
                  {p.isCustom ? <PBadge value="manual_contract" /> : <PBadge value={p.active ? "active" : "cancelled"} />}
                </div>
                <p className="mt-3 text-3xl font-bold text-white">{formatMoney(Number(p.basePriceCents), String(p.currency ?? "USD"))}<span className="text-sm font-normal text-slate-500">/{String(p.billingInterval ?? "mo")}</span></p>
                <div className="mt-3 space-y-1 text-sm text-slate-400">
                  <p>{formatMoney(Number(p.seatPriceCents))} / seat · {String(p.includedSeats ?? 0)} included</p>
                  <p>Setup {formatMoney(Number(p.setupFeeCents))} · Annual {formatMoney(Number(p.annualPriceCents))}</p>
                </div>
                {p.description ? <p className="mt-3 text-xs text-slate-500">{String(p.description)}</p> : null}
                <div className="mt-4 flex flex-wrap gap-1.5">
                  {modules.length === 0 ? <span className="text-xs text-slate-600">No bundled modules</span> :
                    modules.map((m) => <span key={m} className="rounded-full border border-slate-700 bg-slate-800/60 px-2 py-0.5 text-[10px] capitalize text-slate-300">{m.replace(/_/g, " ")}</span>)}
                </div>

                {canManage && (
                  <div className="mt-auto flex flex-wrap gap-2 border-t border-slate-200 pt-4">
                    <PButton variant="ghost" disabled={busy} onClick={() => setEditPkg(p)}>Edit</PButton>
                    <PButton variant="ghost" disabled={busy}
                      onClick={() => run(() => platformApi.updatePackage(Number(p.id), { active: !p.active }), p.active ? "Package deactivated" : "Package activated")}>
                      {p.active ? "Deactivate" : "Activate"}
                    </PButton>
                    <PButton variant="danger" disabled={busy} onClick={() => setDeletePkg(p)}>Delete</PButton>
                  </div>
                )}
              </PCard>
            );
          })}
        </div>
      )}

      {createOpen && (
        <PackageDrawer onClose={() => setCreateOpen(false)} onSaved={() => { setCreateOpen(false); refresh(); setNotice({ ok: true, text: "Package created" }); }} />
      )}
      {editPkg && (
        <PackageDrawer pkg={editPkg} onClose={() => setEditPkg(null)} onSaved={() => { setEditPkg(null); refresh(); setNotice({ ok: true, text: "Package updated" }); }} />
      )}

      <PConfirm
        open={Boolean(deletePkg)}
        title={`Delete “${String(deletePkg?.name ?? "")}”?`}
        body={<>This permanently removes the package. If any tenant is still subscribed to it, the delete is refused — reassign them or deactivate the package instead.</>}
        confirmLabel="Delete package"
        busy={busy}
        onConfirm={() => deletePkg && run(() => platformApi.deletePackage(Number(deletePkg.id)), "Package deleted")}
        onClose={() => setDeletePkg(null)}
      />
    </div>
  );
}

// Handles BOTH create and edit — `pkg` present means edit.
function PackageDrawer({ pkg, onClose, onSaved }: { pkg?: AnyRecord | null; onClose: () => void; onSaved: () => void }) {
  const isEdit = Boolean(pkg?.id);
  const toDollars = (c: unknown) => String((Number(c ?? 0) / 100) || 0);

  const [form, setForm] = useState({
    name: String(pkg?.name ?? ""),
    description: String(pkg?.description ?? ""),
    basePrice: toDollars(pkg?.basePriceCents),
    seatPrice: toDollars(pkg?.seatPriceCents),
    includedSeats: String(pkg?.includedSeats ?? 0),
    setupFee: toDollars(pkg?.setupFeeCents),
    annualPrice: toDollars(pkg?.annualPriceCents),
    isCustom: Boolean(pkg?.isCustom),
    active: pkg?.active === undefined ? true : Boolean(pkg.active),
  });
  const [modules, setModules] = useState<string[]>(() => parseModules(pkg?.moduleKeys));
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const toggle = (m: string) => setModules((cur) => cur.includes(m) ? cur.filter((x) => x !== m) : [...cur, m]);
  const dollarsToCents = (v: string) => Math.round((Number(v) || 0) * 100);

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      const payload: AnyRecord = {
        name: form.name,
        description: form.description || undefined,
        basePriceCents: dollarsToCents(form.basePrice),
        seatPriceCents: dollarsToCents(form.seatPrice),
        includedSeats: Number(form.includedSeats),
        setupFeeCents: dollarsToCents(form.setupFee),
        annualPriceCents: dollarsToCents(form.annualPrice),
        moduleKeys: modules,
      };
      if (isEdit) await platformApi.updatePackage(Number(pkg!.id), { ...payload, active: form.active });
      else await platformApi.createPackage({ ...payload, isCustom: form.isCustom });
      onSaved();
    } catch (e) { setErr(e instanceof Error ? e.message : "Failed"); } finally { setBusy(false); }
  };

  return (
    <PDrawer open onClose={onClose} title={isEdit ? `Edit — ${String(pkg?.name ?? "")}` : "New Package"}>
      {err && <div className="mb-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-700">{err}</div>}
      <div className="space-y-4">
        <PField label="Name"><PInput value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Growth" /></PField>
        <PField label="Description"><PInput value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></PField>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Base price ($/mo)"><PInput type="number" value={form.basePrice} onChange={(e) => setForm({ ...form, basePrice: e.target.value })} /></PField>
          <PField label="Per-seat ($/mo)"><PInput type="number" value={form.seatPrice} onChange={(e) => setForm({ ...form, seatPrice: e.target.value })} /></PField>
          <PField label="Included seats"><PInput type="number" value={form.includedSeats} onChange={(e) => setForm({ ...form, includedSeats: e.target.value })} /></PField>
          <PField label="Setup fee ($)"><PInput type="number" value={form.setupFee} onChange={(e) => setForm({ ...form, setupFee: e.target.value })} /></PField>
          <PField label="Annual price ($)"><PInput type="number" value={form.annualPrice} onChange={(e) => setForm({ ...form, annualPrice: e.target.value })} /></PField>
        </div>
        <PField label="Bundled modules">
          <div className="flex flex-wrap gap-2">
            {MODULE_OPTIONS.map((m) => (
              <button key={m} type="button" onClick={() => toggle(m)}
                className={`rounded-full border px-3 py-1 text-xs capitalize transition ${modules.includes(m) ? "border-teal-400/40 bg-teal-400/10 text-teal-700" : "border-slate-300 bg-white text-slate-500"}`}>
                {m.replace(/_/g, " ")}
              </button>
            ))}
          </div>
        </PField>

        {isEdit ? (
          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />
            Active (available to assign to tenants)
          </label>
        ) : (
          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={form.isCustom} onChange={(e) => setForm({ ...form, isCustom: e.target.checked })} />
            Custom / enterprise package
          </label>
        )}

        <div className="flex gap-2 pt-2">
          <PButton onClick={submit} disabled={busy || !form.name}>{busy ? "Saving…" : isEdit ? "Save changes" : "Create package"}</PButton>
          <PButton variant="ghost" onClick={onClose}>Cancel</PButton>
        </div>
      </div>
    </PDrawer>
  );
}
