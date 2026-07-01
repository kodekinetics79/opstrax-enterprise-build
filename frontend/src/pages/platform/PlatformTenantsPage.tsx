import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Building2 } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import {
  PHeader, PCard, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty, PDrawer,
} from "./ui";

const GATED_MODULES = [
  "safety", "maintenance", "dispatch", "telematics", "crm", "customer_portal", "reports", "compliance",
];

export function PlatformTenantsPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:tenants:manage");
  const canEntitlements = can("platform:entitlements:manage");

  const { data: tenants, isLoading, error } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });
  const { data: packages } = useQuery({ queryKey: ["platform", "packages"], queryFn: platformApi.packages });

  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["platform", "tenants"] });
    qc.invalidateQueries({ queryKey: ["platform", "command-center"] });
    if (selectedId) qc.invalidateQueries({ queryKey: ["platform", "tenant", selectedId] });
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const rows = (tenants ?? []) as AnyRecord[];

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Tenant Management"
        title="Tenants"
        description="Provision accounts, assign packages, control subscription status and feature entitlements."
        actions={canManage ? <PButton onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New Tenant</PButton> : undefined}
      />

      {rows.length === 0 ? (
        <PEmpty title="No tenants yet" subtitle="Create your first tenant to start provisioning the SaaS business." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-left text-sm">
              <thead className="border-b border-slate-800 bg-slate-900/80">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {["Tenant", "Status", "Package", "Seats", "MRR", "Users", "Created"].map((h) => (
                    <th key={h} className="px-5 py-3 font-semibold">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800">
                {rows.map((t) => (
                  <tr
                    key={String(t.id)}
                    onClick={() => setSelectedId(Number(t.id))}
                    className="cursor-pointer transition hover:bg-slate-800/40"
                  >
                    <td className="px-5 py-3.5">
                      <div className="flex items-center gap-3">
                        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-slate-800 text-slate-400">
                          <Building2 className="h-4 w-4" />
                        </div>
                        <div>
                          <p className="font-semibold text-slate-100">{String(t.name)}</p>
                          <p className="text-xs text-slate-500">{String(t.companyCode ?? "—")}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-3.5"><PBadge value={t.status ?? "—"} /></td>
                    <td className="px-5 py-3.5 text-slate-300">{String(t.packageName ?? "—")}</td>
                    <td className="px-5 py-3.5 text-slate-300">{String(t.seatLimit ?? "—")}</td>
                    <td className="px-5 py-3.5 font-semibold text-emerald-400">{formatMoney(Number(t.mrrCents))}</td>
                    <td className="px-5 py-3.5 text-slate-300">{String(t.userCount ?? 0)}</td>
                    <td className="px-5 py-3.5 font-mono text-xs text-slate-500">{String(t.createdAt ?? "").slice(0, 10)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PCard>
      )}

      {createOpen && (
        <CreateTenantDrawer
          packages={(packages ?? []) as AnyRecord[]}
          onClose={() => setCreateOpen(false)}
          onCreated={() => { setCreateOpen(false); refresh(); }}
        />
      )}

      {selectedId !== null && (
        <TenantDetailDrawer
          id={selectedId}
          packages={(packages ?? []) as AnyRecord[]}
          canManage={canManage}
          canEntitlements={canEntitlements}
          gatedModules={GATED_MODULES}
          onClose={() => setSelectedId(null)}
          onChanged={refresh}
        />
      )}
    </div>
  );
}

function CreateTenantDrawer({ packages, onClose, onCreated }: {
  packages: AnyRecord[]; onClose: () => void; onCreated: () => void;
}) {
  const [form, setForm] = useState({ name: "", industry: "Logistics", packageId: "", seatLimit: "5", adminEmail: "", trialDays: "14", countryCode: "" });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  // Country list is server-driven (never hardcoded) so new countries appear here
  // as soon as they are added through the platform country-profile CRUD.
  const { data: countryProfiles } = useQuery({ queryKey: ["platform", "country-profiles"], queryFn: platformApi.countryProfiles });
  const countries = (countryProfiles ?? []) as AnyRecord[];
  const selectedCountry = countries.find((c) => String(c.countryCode) === form.countryCode);
  const autoFeatures = ((selectedCountry?.autoEnabledFeatures ?? []) as unknown[]).map(String);

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      await platformApi.createTenant({
        name: form.name,
        industry: form.industry,
        packageId: form.packageId ? Number(form.packageId) : undefined,
        seatLimit: Number(form.seatLimit),
        adminEmail: form.adminEmail || undefined,
        trialDays: Number(form.trialDays),
        countryCode: form.countryCode || undefined,
        status: "trial",
      });
      onCreated();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Failed to create tenant");
    } finally { setBusy(false); }
  };

  return (
    <PDrawer open onClose={onClose} title="New Tenant">
      {err && <div className="mb-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-300">{err}</div>}
      <div className="space-y-4">
        <PField label="Company name"><PInput value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Acme Logistics" /></PField>
        <PField label="Industry"><PInput value={form.industry} onChange={(e) => setForm({ ...form, industry: e.target.value })} /></PField>
        <PField label="Country">
          <PSelect value={form.countryCode} onChange={(e) => setForm({ ...form, countryCode: e.target.value })}>
            <option value="">— None (defaults: USD, no auto-enabled features) —</option>
            {countries.map((c) => (
              <option key={String(c.countryCode)} value={String(c.countryCode)}>
                {String(c.countryName)} ({String(c.countryCode)})
              </option>
            ))}
          </PSelect>
        </PField>

        {selectedCountry && (
          <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-3 text-sm">
            <p className="mb-2 text-xs font-bold uppercase tracking-wider text-teal-300">On creation, this country will apply</p>
            <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-slate-300">
              <span className="text-slate-500">Currency</span><span className="font-medium text-slate-100">{String(selectedCountry.defaultCurrency)}</span>
              <span className="text-slate-500">Locale</span><span className="font-medium text-slate-100">{String(selectedCountry.defaultLocale)}</span>
              <span className="text-slate-500">Text direction</span><span className="font-medium text-slate-100 uppercase">{String(selectedCountry.textDirection)}</span>
              <span className="text-slate-500">Calendar</span><span className="font-medium text-slate-100">{String(selectedCountry.calendarSystem)}</span>
              <span className="text-slate-500">Invoicing</span><span className="font-medium text-slate-100">{String(selectedCountry.invoicingScheme)}</span>
            </div>
            <p className="mt-3 mb-1.5 text-xs text-slate-400">
              Auto-enabled features {autoFeatures.length === 0 ? "— none" : `(${autoFeatures.length})`}:
            </p>
            {autoFeatures.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {autoFeatures.map((f) => <PBadge key={f} value={f} />)}
              </div>
            )}
            <p className="mt-3 text-[11px] text-slate-500">These are defaults — every feature can still be toggled per-tenant after creation.</p>
          </div>
        )}

        <PField label="Package">
          <PSelect value={form.packageId} onChange={(e) => setForm({ ...form, packageId: e.target.value })}>
            <option value="">— None (trial, no package) —</option>
            {packages.map((p) => <option key={String(p.id)} value={String(p.id)}>{String(p.name)}</option>)}
          </PSelect>
        </PField>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Seat limit"><PInput type="number" value={form.seatLimit} onChange={(e) => setForm({ ...form, seatLimit: e.target.value })} /></PField>
          <PField label="Trial days"><PInput type="number" value={form.trialDays} onChange={(e) => setForm({ ...form, trialDays: e.target.value })} /></PField>
        </div>
        <PField label="Tenant admin email (invite)"><PInput type="email" value={form.adminEmail} onChange={(e) => setForm({ ...form, adminEmail: e.target.value })} placeholder="admin@acme.com" /></PField>
        <div className="flex gap-2 pt-2">
          <PButton onClick={submit} disabled={busy || !form.name}>Create tenant</PButton>
          <PButton variant="ghost" onClick={onClose}>Cancel</PButton>
        </div>
      </div>
    </PDrawer>
  );
}

function TenantDetailDrawer({ id, packages, canManage, canEntitlements, gatedModules, onClose, onChanged }: {
  id: number; packages: AnyRecord[]; canManage: boolean; canEntitlements: boolean; gatedModules: string[];
  onClose: () => void; onChanged: () => void;
}) {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["platform", "tenant", id], queryFn: () => platformApi.tenant(id) });
  const [busy, setBusy] = useState(false);
  const [assignPkg, setAssignPkg] = useState("");

  const reload = () => { qc.invalidateQueries({ queryKey: ["platform", "tenant", id] }); onChanged(); };

  const act = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    try { await fn(); reload(); } finally { setBusy(false); }
  };

  const tenant = (data?.tenant ?? {}) as AnyRecord;
  const entitlements = (data?.entitlements ?? []) as AnyRecord[];
  const entMap = new Map(entitlements.map((e) => [String(e.moduleKey), e]));

  return (
    <PDrawer open onClose={onClose} title={isLoading ? "Loading…" : String(tenant.name ?? "Tenant")}>
      {isLoading ? <PLoading /> : (
        <div className="space-y-6">
          <div className="flex items-center gap-2">
            <PBadge value={tenant.status} />
            <span className="text-xs text-slate-500">{String(tenant.companyCode ?? "")}</span>
          </div>

          <div className="grid grid-cols-2 gap-3 text-sm">
            <Info label="Package" value={String(tenant.packageName ?? "—")} />
            <Info label="MRR" value={formatMoney(Number(tenant.mrrCents))} />
            <Info label="Seat limit" value={String(tenant.seatLimit ?? "—")} />
            <Info label="Users" value={String(tenant.userCount ?? 0)} />
            <Info label="Account owner" value={String(tenant.accountOwner ?? "—")} />
            <Info label="Support owner" value={String(tenant.supportOwner ?? "—")} />
            <Info label="Trial ends" value={String(tenant.trialEndsAt ?? "—").slice(0, 10) || "—"} />
            <Info label="Contract end" value={String(tenant.contractEnd ?? "—").slice(0, 10) || "—"} />
          </div>

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Subscription actions</h3>
              <div className="flex flex-wrap gap-2">
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "activate" }))}>Activate</PButton>
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "extend-trial", days: 14 }))}>Extend trial +14d</PButton>
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "suspend" }))}>Suspend</PButton>
                <PButton variant="danger" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "cancel" }))}>Cancel</PButton>
              </div>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Assign package</h3>
              <div className="flex gap-2">
                <PSelect value={assignPkg} onChange={(e) => setAssignPkg(e.target.value)}>
                  <option value="">— Select package —</option>
                  {packages.map((p) => <option key={String(p.id)} value={String(p.id)}>{String(p.name)}</option>)}
                </PSelect>
                <PButton disabled={busy || !assignPkg} onClick={() => act(() => platformApi.assignPackage(id, { packageId: Number(assignPkg) }))}>Assign</PButton>
              </div>
            </section>
          )}

          <section>
            <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Feature entitlements</h3>
            <p className="mb-3 text-xs text-slate-500">Server-enforced. Disabling a module blocks tenant API access immediately.</p>
            <div className="space-y-2">
              {gatedModules.map((mk) => {
                const ent = entMap.get(mk);
                const enabled = ent ? Boolean(ent.enabled) : true;
                return (
                  <div key={mk} className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-2.5">
                    <div>
                      <p className="text-sm font-medium capitalize text-slate-200">{mk.replace(/_/g, " ")}</p>
                      <p className="text-[11px] text-slate-500">{ent ? `${String(ent.source)} · ${String(ent.tier)}` : "inherited (default on)"}</p>
                    </div>
                    <button
                      disabled={!canEntitlements || busy}
                      onClick={() => act(() => platformApi.setEntitlement(id, { moduleKey: mk, enabled: !enabled }))}
                      className={`relative h-6 w-11 rounded-full transition disabled:opacity-40 ${enabled ? "bg-teal-400" : "bg-slate-700"}`}
                      aria-label={`Toggle ${mk}`}
                    >
                      <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition ${enabled ? "left-[22px]" : "left-0.5"}`} />
                    </button>
                  </div>
                );
              })}
            </div>
          </section>
        </div>
      )}
    </PDrawer>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-slate-900/80 px-3 py-2.5">
      <p className="text-[10px] uppercase tracking-wider text-slate-500">{label}</p>
      <p className="mt-0.5 text-sm font-medium text-slate-200">{value}</p>
    </div>
  );
}
