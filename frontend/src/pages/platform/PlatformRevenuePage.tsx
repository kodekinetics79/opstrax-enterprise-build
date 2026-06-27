import { useEffect, useState } from "react";
import { platformApi, formatMoney } from "@/services/platformApi";
import { PHeader, PCard, PKpi, PButton, PField, PSelect, PLoading, PError, PEmpty } from "./ui";

type AnyRecord = Record<string, any>;

// Platform Admin — Opstrax revenue control panel. Surfaces the module-package
// catalog and usage meters, plus per-tenant usage, invoice preview and contract
// override editing. Tenant subscription/entitlement editing lives on the Tenants
// and Packages screens; this screen is the metering + invoice-preview cockpit.
export function PlatformRevenuePage() {
  const [packages, setPackages] = useState<AnyRecord[]>([]);
  const [meters, setMeters] = useState<AnyRecord[]>([]);
  const [tenants, setTenants] = useState<AnyRecord[]>([]);
  const [tenantId, setTenantId] = useState<number | null>(null);
  const [usage, setUsage] = useState<AnyRecord | null>(null);
  const [invoice, setInvoice] = useState<AnyRecord | null>(null);
  const [marketPacks, setMarketPacks] = useState<AnyRecord[]>([]);
  const [tenantPacks, setTenantPacks] = useState<Record<string, string>>({});
  const [complianceUsage, setComplianceUsage] = useState<AnyRecord | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();

  useEffect(() => {
    (async () => {
      try {
        const [pk, mt, tn, mp] = await Promise.all([
          platformApi.modulePackages(),
          platformApi.meters(),
          platformApi.tenants(),
          platformApi.marketPacks(),
        ]);
        setPackages((pk?.items as AnyRecord[]) ?? []);
        setMeters((mt?.items as AnyRecord[]) ?? []);
        setTenants((tn as AnyRecord[]) ?? []);
        setMarketPacks((mp?.items as AnyRecord[]) ?? []);
        if ((tn ?? []).length > 0) setTenantId(Number(tn[0].id));
      } catch (e: any) {
        setError(e?.message ?? "Failed to load revenue data");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  function reloadTenant(id: number) {
    setBusy(true);
    Promise.all([platformApi.tenantUsage(id), platformApi.invoicePreview(id), platformApi.tenantMarketPacks(id), platformApi.complianceUsage(id)])
      .then(([u, inv, tmp, cu]) => {
        setUsage(u); setInvoice(inv); setComplianceUsage(cu);
        const map: Record<string, string> = {};
        for (const p of ((tmp?.items as AnyRecord[]) ?? [])) map[p.packCode] = p.status;
        setTenantPacks(map);
      })
      .catch((e: any) => setError(e?.message ?? "Failed to load tenant revenue"))
      .finally(() => setBusy(false));
  }

  useEffect(() => { if (tenantId != null) reloadTenant(tenantId); }, [tenantId]);

  function toggleMarketPack(packCode: string, enable: boolean) {
    if (tenantId == null) return;
    setBusy(true);
    platformApi.setTenantMarketPack(tenantId, { packCode, status: enable ? "active" : "disabled" })
      .then(() => reloadTenant(tenantId))
      .catch((e: any) => { setError(e?.message ?? "Failed to update market pack"); setBusy(false); });
  }

  if (loading) return <PLoading />;
  if (error) return <PError message={error} />;

  const lineItems: AnyRecord[] = invoice?.lineItems ?? [];

  return (
    <div className="space-y-6">
      <PHeader
        eyebrow="Opstrax"
        title="Revenue & Usage"
        description="Module packages, usage meters, per-tenant consumption and invoice preview."
      />

      <div className="grid gap-4 sm:grid-cols-3">
        <PKpi label="Module packages" value={String(packages.length)} sub="catalog" />
        <PKpi label="Usage meters" value={String(meters.length)} sub="metered dimensions" />
        <PKpi label="Tenants" value={String(tenants.length)} sub="billable accounts" />
      </div>

      <PCard>
        <h3 className="text-sm font-semibold text-white/90">Module Package Catalog</h3>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm text-white/80">
            <thead className="text-xs uppercase text-white/40">
              <tr><th className="py-2 pr-4">Package</th><th className="py-2 pr-4">Category</th><th className="py-2 pr-4">Core</th><th className="py-2">Modules</th></tr>
            </thead>
            <tbody>
              {packages.map((p) => (
                <tr key={p.packageKey} className="border-t border-white/5">
                  <td className="py-2 pr-4 font-medium text-white/90">{p.name}</td>
                  <td className="py-2 pr-4">{p.category}</td>
                  <td className="py-2 pr-4">{p.isCore ? "Yes" : "—"}</td>
                  <td className="py-2 text-xs text-white/60">{Array.isArray(p.moduleKeys) ? p.moduleKeys.join(", ") : String(p.moduleKeys ?? "")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </PCard>

      <PCard>
        <div className="flex items-center justify-between gap-4">
          <h3 className="text-sm font-semibold text-white/90">Market Packs — {tenants.find((t) => Number(t.id) === tenantId)?.name ?? "tenant"}</h3>
        </div>
        <p className="mt-1 text-xs text-white/40">Paid regional add-ons. Enable/disable per tenant — deny-by-default; tenants cannot self-enable.</p>
        <div className="mt-3 grid gap-3 sm:grid-cols-2">
          {marketPacks.map((p) => {
            const enabled = tenantPacks[p.code] === "active";
            return (
              <div key={p.code} className="rounded-xl border border-white/10 p-3">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="font-medium text-white/90">{p.name}</p>
                    <p className="text-xs text-white/40">{p.region} · {p.defaultCurrency} · {formatMoney(p.basePriceCents, "USD")}/mo</p>
                  </div>
                  <span className={`rounded-full px-2 py-0.5 text-xs ${enabled ? "bg-teal-400/15 text-teal-300" : "bg-white/5 text-white/40"}`}>{enabled ? "Enabled" : "Disabled"}</span>
                </div>
                <div className="mt-3">
                  <PButton variant={enabled ? "danger" : "primary"} disabled={busy || tenantId == null} onClick={() => toggleMarketPack(p.code, !enabled)}>
                    {enabled ? "Disable" : "Enable"}
                  </PButton>
                </div>
              </div>
            );
          })}
        </div>
        {complianceUsage && (
          <div className="mt-4 grid grid-cols-3 gap-3">
            <PKpi label="Compliance docs" value={String(complianceUsage?.totals?.complianceDocuments ?? 0)} />
            <PKpi label="Inspections" value={String(complianceUsage?.totals?.inspections ?? 0)} />
            <PKpi label="Expiry events" value={String(complianceUsage?.totals?.expiryEvents ?? 0)} />
          </div>
        )}
      </PCard>

      <PCard>
        <div className="flex items-center justify-between gap-4">
          <h3 className="text-sm font-semibold text-white/90">Tenant Usage & Invoice Preview</h3>
          <div className="w-64">
            <PField label="Tenant">
              <PSelect value={tenantId ?? ""} onChange={(e) => setTenantId(Number(e.target.value))}>
                {tenants.map((t) => (<option key={t.id} value={t.id}>{t.name ?? `Tenant ${t.id}`}</option>))}
              </PSelect>
            </PField>
          </div>
        </div>

        {busy ? <PLoading /> : (
          <div className="mt-4 grid gap-6 lg:grid-cols-2">
            <div>
              <h4 className="text-xs uppercase text-white/40">Usage — {usage?.period ?? "current period"}</h4>
              {(usage?.meters ?? []).length === 0 ? <PEmpty title="No usage recorded yet" /> : (
                <table className="mt-2 w-full text-left text-sm text-white/80">
                  <thead className="text-xs uppercase text-white/40">
                    <tr><th className="py-1 pr-4">Meter</th><th className="py-1 pr-4">Used</th><th className="py-1">Limit</th></tr>
                  </thead>
                  <tbody>
                    {(usage?.meters ?? []).map((m: AnyRecord) => (
                      <tr key={m.meterKey} className="border-t border-white/5">
                        <td className="py-1 pr-4">{m.name ?? m.meterKey}</td>
                        <td className="py-1 pr-4 font-medium text-white/90">{Number(m.value ?? 0)}</td>
                        <td className="py-1 text-white/50">{m.limitValue ?? "∞"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div>
              <h4 className="text-xs uppercase text-white/40">Invoice Preview</h4>
              {lineItems.length === 0 ? <PEmpty title="No billable lines" subtitle={invoice?.note} /> : (
                <table className="mt-2 w-full text-left text-sm text-white/80">
                  <thead className="text-xs uppercase text-white/40">
                    <tr><th className="py-1 pr-4">Line</th><th className="py-1 pr-4">Qty</th><th className="py-1 text-right">Amount</th></tr>
                  </thead>
                  <tbody>
                    {lineItems.map((li, i) => (
                      <tr key={i} className="border-t border-white/5">
                        <td className="py-1 pr-4">{li.description}</td>
                        <td className="py-1 pr-4">{Number(li.quantity ?? 0)}</td>
                        <td className="py-1 text-right">{formatMoney(li.amountCents, invoice?.currency)}</td>
                      </tr>
                    ))}
                    <tr className="border-t border-white/15 font-semibold text-white">
                      <td className="py-2 pr-4" colSpan={2}>Total</td>
                      <td className="py-2 text-right">{formatMoney(invoice?.totalCents, invoice?.currency)}</td>
                    </tr>
                  </tbody>
                </table>
              )}
              <div className="mt-3">
                <PButton variant="ghost" onClick={() => { if (tenantId != null) platformApi.invoicePreview(tenantId).then(setInvoice); }} disabled={tenantId == null}>
                  Refresh preview
                </PButton>
              </div>
            </div>
          </div>
        )}
      </PCard>
    </div>
  );
}
