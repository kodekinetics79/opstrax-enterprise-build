import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { AnyRecord } from "@/types";
import { formatAmount, minorUnits, platformApi } from "@/services/platformApi";
import { PButton, PField, PInput, PSelect } from "../ui";

const CHARGE_MODEL_HELP: Record<string, string> = {
  free: "Granted at no charge — appears on the invoice at zero so the giveaway is visible",
  included: "Bundled in the subscription base price — no separate line",
  flat: "Fixed recurring amount per billing interval",
  per_seat: "Unit price × active users above the included allowance",
  per_unit: "Unit price × metered usage above the included allowance (event based)",
  tiered: "Graduated tiers — each band prices only the quantity inside it",
  one_time: "Charged once, in the period its effective date falls in",
};

const EMPTY_PLAN_FORM = {
  featureKey: "", featureLabel: "", chargeModel: "flat", meterKey: "",
  unitPrice: "0", includedQuantity: "0", flatPrice: "0", minimum: "", cap: "", note: "",
};

export function TenantBillingPlanSection({ tenantId, canManage, onNotice }: {
  tenantId: number; canManage: boolean; onNotice: (msg: string) => void;
}) {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({
    queryKey: ["platform", "tenant", tenantId, "billing-plan"],
    queryFn: () => platformApi.billingPlan(tenantId),
  });
  const [form, setForm] = useState({ ...EMPTY_PLAN_FORM });
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const items = ((data as AnyRecord)?.items ?? []) as AnyRecord[];
  const catalog = ((data as AnyRecord)?.catalog ?? {}) as AnyRecord;
  const meters = (catalog.meters ?? []) as AnyRecord[];
  const modules = (catalog.modules ?? []) as AnyRecord[];
  const marketPacks = (catalog.marketPacks ?? []) as AnyRecord[];
  const currency = String((data as AnyRecord)?.currency ?? "USD");
  const scale = 10 ** minorUnits(currency);
  const usesMeter = form.chargeModel === "per_unit" || form.chargeModel === "tiered";

  const reload = () => qc.invalidateQueries({ queryKey: ["platform", "tenant", tenantId, "billing-plan"] });

  const save = async () => {
    setBusy(true); setErr(null);
    try {
      await platformApi.setBillingPlanItem(tenantId, {
        featureKey: form.featureKey.trim(),
        featureLabel: form.featureLabel.trim() || undefined,
        chargeModel: form.chargeModel,
        meterKey: usesMeter ? form.meterKey : undefined,
        unitPriceCents: Math.round((Number(form.unitPrice) || 0) * scale),
        includedQuantity: Number(form.includedQuantity) || 0,
        flatPriceCents: Math.round((Number(form.flatPrice) || 0) * scale),
        minimumCents: form.minimum === "" ? undefined : Math.round(Number(form.minimum) * scale),
        capCents: form.cap === "" ? undefined : Math.round(Number(form.cap) * scale),
        note: form.note.trim() || undefined,
      });
      setForm({ ...EMPTY_PLAN_FORM });
      setOpen(false);
      onNotice(`Billing term saved for ${form.featureKey.trim()}`);
      reload();
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not save the term"); }
    finally { setBusy(false); }
  };

  const remove = async (featureKey: string) => {
    setBusy(true); setErr(null);
    try {
      await platformApi.deleteBillingPlanItem(tenantId, featureKey);
      onNotice(`${featureKey} removed — it falls back to its package default`);
      reload();
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not remove the term"); }
    finally { setBusy(false); }
  };

  const describe = (i: AnyRecord) => {
    const model = String(i.chargeModel);
    const unit = formatAmount(Number(i.unitPriceCents ?? 0), currency);
    const flat = formatAmount(Number(i.flatPriceCents ?? 0), currency);
    const incl = Number(i.includedQuantity ?? 0);
    switch (model) {
      case "free": return "Free of charge";
      case "included": return "Bundled in the subscription";
      case "flat": return `${flat} per ${String(i.billingInterval ?? "monthly")}`;
      case "per_seat": return `${unit} per seat${incl > 0 ? ` after ${incl} included` : ""}`;
      case "per_unit": return `${unit} per ${String(i.meterKey ?? "event")}${incl > 0 ? ` after ${incl} included` : ""}`;
      case "tiered": return `Tiered on ${String(i.meterKey ?? "usage")}`;
      case "one_time": return `${flat} once`;
      default: return model;
    }
  };

  return (
    <section>
      <div className="mb-2 flex items-center justify-between">
        <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Billing plan — per feature</h3>
        {canManage && (
          <PButton variant="ghost" onClick={() => { setOpen((v) => !v); setErr(null); }}>
            {open ? "Cancel" : "Add term"}
          </PButton>
        )}
      </div>
      <p className="mb-3 text-[11px] leading-5 text-slate-500">
        Anything listed here overrides the package default for this tenant only. A feature can be free, a flat fee,
        priced per seat, billed per event against a meter, or tiered — with an optional floor and cap.
      </p>

      {err && <p className="mb-2 text-xs font-medium text-red-600">{err}</p>}

      {open && canManage && (
        <div className="mb-3 space-y-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-2 gap-3">
            <PField label="Feature key">
              <PInput
                list={`feature-catalog-${tenantId}`}
                value={form.featureKey}
                onChange={(e) => setForm({ ...form, featureKey: e.target.value })}
                placeholder="fleet.dispatch"
              />
              <datalist id={`feature-catalog-${tenantId}`}>
                {modules.map((m) => <option key={String(m.key)} value={String(m.key)} />)}
                {marketPacks.map((m) => <option key={String(m.key)} value={`market_pack.${String(m.key)}`}>{String(m.label)}</option>)}
              </datalist>
            </PField>
            <PField label="Label on the invoice">
              <PInput value={form.featureLabel} onChange={(e) => setForm({ ...form, featureLabel: e.target.value })} placeholder="Dispatch module" />
            </PField>
          </div>

          <PField label="Charge model">
            <PSelect value={form.chargeModel} onChange={(e) => setForm({ ...form, chargeModel: e.target.value })}>
              {Object.keys(CHARGE_MODEL_HELP).map((m) => <option key={m} value={m}>{m.replace(/_/g, " ")}</option>)}
            </PSelect>
          </PField>
          <p className="text-[11px] text-slate-500">{CHARGE_MODEL_HELP[form.chargeModel]}</p>

          {usesMeter && (
            <PField label="Meter (what the usage is counted from)">
              <PSelect value={form.meterKey} onChange={(e) => setForm({ ...form, meterKey: e.target.value })}>
                <option value="">— Select meter —</option>
                {meters.map((m) => <option key={String(m.key)} value={String(m.key)}>{String(m.label)} ({String(m.key)})</option>)}
              </PSelect>
            </PField>
          )}

          <div className="grid grid-cols-3 gap-3">
            {(form.chargeModel === "flat" || form.chargeModel === "one_time") ? (
              <PField label={`Amount (${currency})`}>
                <PInput type="number" min={0} step="any" value={form.flatPrice} onChange={(e) => setForm({ ...form, flatPrice: e.target.value })} />
              </PField>
            ) : (
              <PField label={`Unit price (${currency})`}>
                <PInput type="number" min={0} step="any" value={form.unitPrice}
                        disabled={form.chargeModel === "free" || form.chargeModel === "included"}
                        onChange={(e) => setForm({ ...form, unitPrice: e.target.value })} />
              </PField>
            )}
            <PField label="Included quantity">
              <PInput type="number" min={0} step="any" value={form.includedQuantity}
                      disabled={form.chargeModel === "free" || form.chargeModel === "included" || form.chargeModel === "flat" || form.chargeModel === "one_time"}
                      onChange={(e) => setForm({ ...form, includedQuantity: e.target.value })} />
            </PField>
            <PField label={`Cap (${currency})`}>
              <PInput type="number" min={0} step="any" value={form.cap} placeholder="none"
                      onChange={(e) => setForm({ ...form, cap: e.target.value })} />
            </PField>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <PField label={`Minimum charge (${currency})`}>
              <PInput type="number" min={0} step="any" value={form.minimum} placeholder="none"
                      onChange={(e) => setForm({ ...form, minimum: e.target.value })} />
            </PField>
            <PField label="Note (why this term exists)">
              <PInput value={form.note} maxLength={400} onChange={(e) => setForm({ ...form, note: e.target.value })}
                      placeholder="e.g. Waived for the 2026 pilot per signed order" />
            </PField>
          </div>

          <PButton
            disabled={busy || !form.featureKey.trim() || (usesMeter && !form.meterKey)}
            onClick={save}
          >
            {busy ? "Saving…" : "Save term"}
          </PButton>
        </div>
      )}

      {isLoading ? <p className="text-xs text-slate-500">Loading billing plan…</p>
        : items.length === 0 ? (
          <p className="text-xs text-slate-500">
            No per-feature terms — this tenant is billed entirely on its package and active market packs.
          </p>
        ) : (
          <div className="space-y-2">
            {items.map((i) => (
              <div key={String(i.featureKey)} className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2.5">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-slate-800">
                    {String(i.featureLabel ?? i.featureKey)}
                    {String(i.chargeModel) === "free" && (
                      <span className="ml-2 rounded bg-emerald-50 px-1.5 py-0.5 text-[10px] font-bold uppercase text-emerald-700">Free</span>
                    )}
                    {!i.active && (
                      <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-bold uppercase text-slate-500">Inactive</span>
                    )}
                  </p>
                  <p className="truncate text-[11px] text-slate-500">
                    <span className="font-mono">{String(i.featureKey)}</span> · {describe(i)}
                    {i.capCents != null && ` · capped at ${formatAmount(Number(i.capCents), currency)}`}
                    {i.note ? ` · ${String(i.note)}` : ""}
                  </p>
                </div>
                {canManage && (
                  <PButton variant="ghost" disabled={busy} onClick={() => remove(String(i.featureKey))}>Remove</PButton>
                )}
              </div>
            ))}
          </div>
        )}
    </section>
  );
}
