import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { platformApi, formatAmount, formatRate } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PHeader, PCard, PKpi, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty } from "./ui";

type AnyRecord = Record<string, any>;

// Platform Admin — Opstrax revenue cockpit. This screen answers four questions and
// lets you act on each: what is contracted, what has actually been billed and
// collected, what usage is actually moving, and what the next invoice for a given
// tenant will look like. Catalog browsing is secondary and lives at the bottom.
export function PlatformRevenuePage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:billing:manage");

  const summaryQ = useQuery({ queryKey: ["platform", "revenue-summary"], queryFn: platformApi.revenueSummary });
  const packagesQ = useQuery({ queryKey: ["platform", "module-packages"], queryFn: platformApi.modulePackages });
  const marketPacksQ = useQuery({ queryKey: ["platform", "market-packs"], queryFn: platformApi.marketPacks });

  const [tenantId, setTenantId] = useState<number | null>(null);
  const [usage, setUsage] = useState<AnyRecord | null>(null);
  const [preview, setPreview] = useState<AnyRecord | null>(null);
  const [tenantPacks, setTenantPacks] = useState<Record<string, string>>({});
  const [complianceUsage, setComplianceUsage] = useState<AnyRecord | null>(null);
  const [marketPackReason, setMarketPackReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const [notice, setNotice] = useState<string | null>(null);

  const summary = summaryQ.data as AnyRecord | undefined;
  const totals = (summary?.totals ?? {}) as AnyRecord;
  const byCurrency = (summary?.byCurrency ?? []) as AnyRecord[];
  // The portfolio is multi-currency, so the headline row is per currency rather
  // than one blended number with a dollar sign in front of it.
  const lead = byCurrency[0] ?? {};
  const leadCurrency = String(lead.currency ?? "USD");
  const tenants = useMemo(() => (summary?.tenants ?? []) as AnyRecord[], [summary]);
  const meterActivity = (summary?.meterActivity ?? []) as AnyRecord[];
  const recentDocs = (summary?.recentDocuments ?? []) as AnyRecord[];
  const packages = (packagesQ.data?.items ?? []) as AnyRecord[];
  const marketPacks = (marketPacksQ.data?.items ?? []) as AnyRecord[];

  useEffect(() => {
    if (tenantId === null && tenants.length > 0) setTenantId(Number(tenants[0].id));
  }, [tenants, tenantId]);

  function reloadTenant(id: number) {
    setBusy(true); setError(undefined);
    Promise.all([
      platformApi.tenantUsage(id),
      platformApi.previewInvoice({ companyId: id }),
      platformApi.tenantMarketPacks(id),
      platformApi.complianceUsage(id),
    ])
      .then(([u, inv, tmp, cu]) => {
        setUsage(u); setPreview(inv); setComplianceUsage(cu);
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
    const reason = marketPackReason.trim();
    if (!reason) return;
    setBusy(true);
    platformApi.setTenantMarketPack(tenantId, { packCode, status: enable ? "active" : "disabled", reason })
      .then(() => { setMarketPackReason(""); reloadTenant(tenantId); })
      .catch((e: any) => { setError(e?.message ?? "Failed to update market pack"); setBusy(false); });
  }

  async function generateFromPreview(issue: boolean) {
    if (tenantId == null) return;
    setBusy(true); setNotice(null); setError(undefined);
    try {
      const res = await platformApi.generateInvoice({ companyId: tenantId, issue });
      setNotice(issue ? `Invoice ${res.invoiceNumber} issued` : "Draft invoice created — review it under Billing & Invoices");
      qc.invalidateQueries({ queryKey: ["platform", "revenue-summary"] });
      qc.invalidateQueries({ queryKey: ["platform", "invoices"] });
    } catch (e: any) {
      setError(e?.message ?? "Could not generate the invoice");
    } finally { setBusy(false); }
  }

  if (summaryQ.isLoading) return <PLoading />;
  if (summaryQ.error) return <PError message={(summaryQ.error as Error)?.message} />;

  const lines = (preview?.lines ?? []) as AnyRecord[];
  const previewTax = preview?.tax as AnyRecord | undefined;
  const previewCurrency = String(preview?.currency ?? "USD");
  const selected = tenants.find((t) => Number(t.id) === tenantId);

  return (
    <div className="space-y-6">
      <PHeader
        eyebrow="Opstrax"
        title="Revenue & Usage"
        description="Contracted MRR against what has actually been billed and collected, live usage metering, and the next invoice for any tenant — generated from their billing plan."
      />

      {error && <PError message={error} />}
      {notice && <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{notice}</div>}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi
          label={`Contracted MRR · ${leadCurrency}`}
          value={formatAmount(Number(lead.contractedMrr ?? 0), leadCurrency)}
          sub={`${Number(totals.payingTenants ?? 0)} paying · ${Number(totals.trialTenants ?? 0)} in trial`}
        />
        <PKpi
          label={`Collected · ${leadCurrency}`}
          value={formatAmount(Number(lead.collected ?? 0), leadCurrency)}
          tone="good"
          sub="paid documents, lifetime"
        />
        <PKpi
          label={`Outstanding · ${leadCurrency}`}
          value={formatAmount(Number(lead.outstanding ?? 0), leadCurrency)}
          tone={Number(lead.outstanding ?? 0) > 0 ? "warn" : "default"}
          sub={`${Number(totals.drafts ?? 0)} draft${Number(totals.drafts ?? 0) === 1 ? "" : "s"} not yet issued`}
        />
        <PKpi
          label={`Tax billed · ${leadCurrency}`}
          value={formatAmount(Number(lead.taxBilled ?? 0), leadCurrency)}
          sub={`registered in ${Number(totals.registeredCountries ?? 0)} countr${Number(totals.registeredCountries ?? 0) === 1 ? "y" : "ies"}`}
        />
      </div>

      {byCurrency.length > 1 && (
        <PCard className="overflow-hidden">
          <div className="px-5 pt-4">
            <h3 className="text-sm font-semibold text-slate-900">All currencies</h3>
            <p className="mt-1 text-xs text-slate-500">
              Held separately, not blended. Converting to one reporting currency needs an FX rate per invoice date, which is not captured yet.
            </p>
          </div>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-y border-slate-200 bg-slate-50 text-xs uppercase tracking-wider text-slate-500">
                <tr>
                  <th className="px-5 py-2.5 font-semibold">Currency</th>
                  <th className="px-5 py-2.5 font-semibold">Contracted MRR</th>
                  <th className="px-5 py-2.5 font-semibold">Collected</th>
                  <th className="px-5 py-2.5 font-semibold">Outstanding</th>
                  <th className="px-5 py-2.5 font-semibold">Tax billed</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {byCurrency.map((c) => {
                  const cur = String(c.currency);
                  return (
                    <tr key={cur}>
                      <td className="px-5 py-2.5 font-semibold text-slate-900">{cur}</td>
                      <td className="px-5 py-2.5 text-slate-700">{formatAmount(Number(c.contractedMrr ?? 0), cur)}</td>
                      <td className="px-5 py-2.5 text-emerald-700">{formatAmount(Number(c.collected ?? 0), cur)}</td>
                      <td className="px-5 py-2.5 text-amber-700">{formatAmount(Number(c.outstanding ?? 0), cur)}</td>
                      <td className="px-5 py-2.5 text-slate-600">{formatAmount(Number(c.taxBilled ?? 0), cur)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </PCard>
      )}

      {/* ── Per-tenant revenue ─────────────────────────────────────────────── */}
      <PCard className="overflow-hidden">
        <div className="px-5 pt-5">
          <h3 className="text-sm font-semibold text-slate-900">Revenue by tenant</h3>
          <p className="mt-1 text-xs text-slate-500">
            Contracted MRR beside what each account has actually paid. A tenant with plan items priced at zero is a
            deliberate giveaway — it shows here rather than hiding inside a package.
          </p>
        </div>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[880px] text-left text-sm">
            <thead className="border-y border-slate-200 bg-slate-50 text-xs uppercase tracking-wider text-slate-500">
              <tr>
                <th className="px-5 py-3 font-semibold">Tenant</th>
                <th className="px-5 py-3 font-semibold">Status</th>
                <th className="px-5 py-3 font-semibold">Package</th>
                <th className="px-5 py-3 font-semibold">MRR</th>
                <th className="px-5 py-3 font-semibold">Plan terms</th>
                <th className="px-5 py-3 font-semibold">Collected</th>
                <th className="px-5 py-3 font-semibold">Outstanding</th>
                <th className="px-5 py-3 font-semibold">Last invoiced</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200">
              {tenants.map((t) => {
                const cur = String(t.billingCurrency ?? "USD");
                const free = Number(t.freeItems ?? 0);
                return (
                  <tr
                    key={String(t.id)}
                    className={`cursor-pointer hover:bg-slate-50 ${Number(t.id) === tenantId ? "bg-teal-50" : ""}`}
                    onClick={() => setTenantId(Number(t.id))}
                  >
                    <td className="px-5 py-3">
                      <span className="font-medium text-slate-900">{String(t.name)}</span>
                      {t.country ? <span className="ml-2 text-[10px] font-semibold text-slate-400">{String(t.country)}</span> : null}
                    </td>
                    <td className="px-5 py-3"><PBadge value={t.status ?? "—"} /></td>
                    <td className="px-5 py-3 text-slate-600">{String(t.packageName ?? "—")}</td>
                    <td className="px-5 py-3 font-medium text-slate-900">{formatAmount(Number(t.mrrCents ?? 0), cur)}</td>
                    <td className="px-5 py-3 text-slate-600">
                      {Number(t.planItems ?? 0) === 0 ? <span className="text-slate-400">package default</span> : (
                        <>
                          {Number(t.planItems)} item{Number(t.planItems) === 1 ? "" : "s"}
                          {free > 0 && <span className="ml-1.5 text-[10px] font-bold uppercase text-emerald-600">{free} free</span>}
                        </>
                      )}
                    </td>
                    <td className="px-5 py-3 text-emerald-700">{formatAmount(Number(t.collected ?? 0), cur)}</td>
                    <td className="px-5 py-3 text-amber-700">{formatAmount(Number(t.outstanding ?? 0), cur)}</td>
                    <td className="px-5 py-3 font-mono text-xs text-slate-500">{String(t.lastInvoicedAt ?? "").slice(0, 10) || "never"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </PCard>

      {/* ── Next invoice for the selected tenant ───────────────────────────── */}
      <PCard className="p-5">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h3 className="text-sm font-semibold text-slate-900">Next invoice — {String(selected?.name ?? "select a tenant")}</h3>
            <p className="mt-1 text-xs text-slate-500">
              Built live from the subscription, the tenant's billing plan, active market packs and metered overage, then taxed
              on the country of activation.
            </p>
          </div>
          <div className="w-64">
            <PField label="Tenant">
              <PSelect value={tenantId ?? ""} onChange={(e) => setTenantId(Number(e.target.value))}>
                {tenants.map((t) => <option key={String(t.id)} value={String(t.id)}>{String(t.name)}</option>)}
              </PSelect>
            </PField>
          </div>
        </div>

        {busy ? <PLoading /> : (
          <div className="mt-4 grid gap-6 lg:grid-cols-2">
            {/* Usage */}
            <div>
              <h4 className="text-[10px] font-black uppercase tracking-[0.2em] text-slate-400">
                Usage — {String(usage?.period ?? "current period")}
              </h4>
              {((usage?.meters ?? []) as AnyRecord[]).filter((m) => Number(m.value ?? 0) > 0).length === 0 ? (
                <PEmpty
                  title="No metered usage this period"
                  subtitle="Meters record as tenants act — proof of delivery, tracking links, fuel transactions and invoice-ready shipments."
                />
              ) : (
                <table className="mt-2 w-full text-left text-sm">
                  <thead className="text-[10px] uppercase tracking-wider text-slate-400">
                    <tr><th className="py-1 pr-4">Meter</th><th className="py-1 pr-4">Used</th><th className="py-1">Limit</th></tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {((usage?.meters ?? []) as AnyRecord[])
                      .filter((m) => Number(m.value ?? 0) > 0)
                      .map((m) => (
                        <tr key={String(m.meterKey)}>
                          <td className="py-1.5 pr-4 text-slate-700">{String(m.name ?? m.meterKey)}</td>
                          <td className="py-1.5 pr-4 font-medium text-slate-900">{Number(m.value ?? 0).toLocaleString()}</td>
                          <td className="py-1.5 text-slate-500">{m.limitValue ?? "∞"}</td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              )}
            </div>

            {/* Invoice preview */}
            <div>
              <h4 className="text-[10px] font-black uppercase tracking-[0.2em] text-slate-400">Invoice preview</h4>
              {previewTax && (
                <p className="mt-1 text-[11px] text-slate-500">
                  {String(previewTax.countryName)} · {formatRate(Number(previewTax.rate ?? 0))} {String(previewTax.taxLabel)} ·{" "}
                  <span className="font-mono">{String(previewTax.ruleKey)}</span>
                </p>
              )}
              {lines.length === 0 ? (
                <PEmpty title="No billable lines" subtitle={((preview?.notes ?? []) as string[])[0]} />
              ) : (
                <>
                  <table className="mt-2 w-full text-left text-sm">
                    <thead className="text-[10px] uppercase tracking-wider text-slate-400">
                      <tr><th className="py-1 pr-4">Line</th><th className="py-1 pr-4">Qty</th><th className="py-1 text-right">Amount</th></tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {lines.map((li, i) => (
                        <tr key={i}>
                          <td className="py-1.5 pr-4 text-slate-700">
                            {String(li.description)}
                            <span className="ml-1.5 text-[10px] uppercase text-slate-400">{String(li.chargeModel)}</span>
                          </td>
                          <td className="py-1.5 pr-4 text-slate-500">{Number(li.quantity ?? 0).toLocaleString()}</td>
                          <td className="py-1.5 text-right text-slate-800">{formatAmount(Number(li.totalCents ?? 0), previewCurrency)}</td>
                        </tr>
                      ))}
                    </tbody>
                    <tfoot>
                      <tr className="border-t border-slate-200 text-slate-600">
                        <td className="py-1.5" colSpan={2}>Net</td>
                        <td className="py-1.5 text-right">{formatAmount(Number(preview?.subtotalCents ?? 0), previewCurrency)}</td>
                      </tr>
                      <tr className="text-slate-600">
                        <td className="py-1" colSpan={2}>{String(previewTax?.taxLabel ?? "Tax")}</td>
                        <td className="py-1 text-right">{formatAmount(Number(preview?.taxTotalCents ?? 0), previewCurrency)}</td>
                      </tr>
                      <tr className="border-t border-slate-300 font-black text-slate-950">
                        <td className="py-2" colSpan={2}>Total</td>
                        <td className="py-2 text-right">{formatAmount(Number(preview?.totalCents ?? 0), previewCurrency)}</td>
                      </tr>
                    </tfoot>
                  </table>

                  {((preview?.notes ?? []) as string[]).map((n, i) => (
                    <p key={i} className="mt-2 rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1.5 text-[11px] text-amber-800">{n}</p>
                  ))}
                </>
              )}

              <div className="mt-3 flex flex-wrap gap-2">
                <PButton variant="ghost" onClick={() => tenantId != null && reloadTenant(tenantId)} disabled={tenantId == null}>
                  Refresh
                </PButton>
                {canManage && (
                  <>
                    <PButton disabled={tenantId == null || lines.length === 0} onClick={() => generateFromPreview(false)}>
                      Create draft
                    </PButton>
                    <PButton variant="ghost" disabled={tenantId == null || lines.length === 0} onClick={() => generateFromPreview(true)}>
                      Generate &amp; issue
                    </PButton>
                  </>
                )}
              </div>
            </div>
          </div>
        )}
      </PCard>

      {/* ── Metering health ────────────────────────────────────────────────── */}
      <PCard className="p-5">
        <h3 className="text-sm font-semibold text-slate-900">Metering</h3>
        <p className="mt-1 text-xs text-slate-500">
          Consumption recorded across all tenants this period. A meter reading zero everywhere is not yet wired to an
          action — pricing anything against it would bill nothing.
        </p>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[560px] text-left text-sm">
            <thead className="text-[10px] uppercase tracking-wider text-slate-400">
              <tr>
                <th className="py-2 pr-4 font-semibold">Meter</th>
                <th className="py-2 pr-4 font-semibold">Unit</th>
                <th className="py-2 pr-4 font-semibold">Period</th>
                <th className="py-2 pr-4 font-semibold">Total</th>
                <th className="py-2 font-semibold">Tenants active</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {meterActivity.map((m) => {
                const total = Number(m.totalValue ?? 0);
                return (
                  <tr key={String(m.meterKey)} className={total === 0 ? "text-slate-400" : ""}>
                    <td className="py-1.5 pr-4">
                      <span className={total === 0 ? "" : "font-medium text-slate-900"}>{String(m.name ?? m.meterKey)}</span>
                      <span className="ml-2 font-mono text-[10px] text-slate-400">{String(m.meterKey)}</span>
                    </td>
                    <td className="py-1.5 pr-4">{String(m.unit ?? "count")}</td>
                    <td className="py-1.5 pr-4">{String(m.period ?? "monthly")}</td>
                    <td className={`py-1.5 pr-4 ${total > 0 ? "font-semibold text-slate-900" : ""}`}>{total.toLocaleString()}</td>
                    <td className="py-1.5">{Number(m.activeTenants ?? 0)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </PCard>

      {/* ── Market packs ───────────────────────────────────────────────────── */}
      <PCard className="p-5">
        <h3 className="text-sm font-semibold text-slate-900">Market packs — {String(selected?.name ?? "tenant")}</h3>
        <p className="mt-1 text-xs text-slate-500">
          Paid regional add-ons, deny-by-default. Enabling one adds a recurring line to that tenant's next invoice.
        </p>
        {canManage && (
          <div className="mt-3 max-w-xl">
            <PField label="Operator reason (recorded in Platform audit)">
              <PInput
                value={marketPackReason}
                maxLength={500}
                placeholder="e.g. Approved pilot add-on under signed order"
                onChange={(event) => setMarketPackReason(event.target.value)}
              />
            </PField>
          </div>
        )}
        <div className="mt-3 grid gap-3 sm:grid-cols-2">
          {marketPacks.map((p) => {
            const enabled = tenantPacks[p.code] === "active";
            return (
              <div key={String(p.code)} className="rounded-xl border border-slate-200 p-3">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="font-medium text-slate-900">{String(p.name)}</p>
                    <p className="text-xs text-slate-500">
                      {String(p.region)} · {String(p.defaultCurrency)} · {formatAmount(Number(p.basePriceCents ?? 0), "USD")}/mo
                    </p>
                  </div>
                  <PBadge value={enabled ? "active" : "disabled"} />
                </div>
                {canManage && (
                  <div className="mt-3">
                    <PButton
                      variant={enabled ? "danger" : "primary"}
                      disabled={busy || tenantId == null || !marketPackReason.trim()}
                      onClick={() => toggleMarketPack(String(p.code), !enabled)}
                    >
                      {enabled ? "Disable" : "Enable"}
                    </PButton>
                  </div>
                )}
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

      {/* ── Recent documents + catalog ─────────────────────────────────────── */}
      <div className="grid gap-6 lg:grid-cols-2">
        <PCard className="p-5">
          <h3 className="text-sm font-semibold text-slate-900">Recent documents</h3>
          {recentDocs.length === 0 ? (
            <PEmpty title="Nothing invoiced yet" subtitle="Generate a period above to raise the first document." />
          ) : (
            <table className="mt-3 w-full text-left text-sm">
              <tbody className="divide-y divide-slate-100">
                {recentDocs.map((d) => (
                  <tr key={String(d.id)}>
                    <td className="py-1.5 pr-3 font-mono text-xs text-slate-600">{String(d.invoiceNumber)}</td>
                    <td className="py-1.5 pr-3 text-slate-700">{String(d.tenant)}</td>
                    <td className="py-1.5 pr-3"><PBadge value={d.status} /></td>
                    <td className="py-1.5 text-right font-medium text-slate-900">
                      {formatAmount(Number(d.totalCents ?? 0), String(d.currency ?? "USD"))}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </PCard>

        <PCard className="p-5">
          <h3 className="text-sm font-semibold text-slate-900">Module package catalog</h3>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-[10px] uppercase tracking-wider text-slate-400">
                <tr>
                  <th className="py-2 pr-4 font-semibold">Package</th>
                  <th className="py-2 pr-4 font-semibold">Category</th>
                  <th className="py-2 font-semibold">Core</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {packages.map((p) => (
                  <tr key={String(p.packageKey)}>
                    <td className="py-1.5 pr-4 font-medium text-slate-800">{String(p.name)}</td>
                    <td className="py-1.5 pr-4 text-slate-600">{String(p.category)}</td>
                    <td className="py-1.5 text-slate-500">{p.isCore ? "Yes" : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PCard>
      </div>
    </div>
  );
}
