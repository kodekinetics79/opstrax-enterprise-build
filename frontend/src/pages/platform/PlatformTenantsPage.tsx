import { useMemo, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Building2, Search } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import {
  PHeader, PCard, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty, PDrawer, PConfirm,
  PCheckbox, PBulkBar, useRowSelection,
} from "./ui";

const GATED_MODULES = [
  "safety", "maintenance", "dispatch", "telematics", "crm", "customer_portal", "reports", "compliance",
];

export function PlatformTenantsPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:tenants:manage");
  const canOffboard = can("platform:tenants:offboard");
  const canEntitlements = can("platform:entitlements:manage");

  const { data: tenants, isLoading, error } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });
  const { data: packages } = useQuery({ queryKey: ["platform", "packages"], queryFn: platformApi.packages });

  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["platform", "tenants"] });
    qc.invalidateQueries({ queryKey: ["platform", "command-center"] });
    if (selectedId) qc.invalidateQueries({ queryKey: ["platform", "tenant", selectedId] });
  };

  const all = (tenants ?? []) as AnyRecord[];
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return all.filter((t) => {
      if (statusFilter && String(t.status ?? "") !== statusFilter) return false;
      if (!q) return true;
      return [t.name, t.companyCode, t.packageName, t.industry]
        .some((v) => String(v ?? "").toLowerCase().includes(q));
    });
  }, [all, search, statusFilter]);

  const sel = useRowSelection(rows.map((t) => Number(t.id)));
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkConfirm, setBulkConfirm] = useState<"suspend" | "cancel" | "delete" | null>(null);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);

  const runBulk = async (action: string, extra?: AnyRecord) => {
    setBulkBusy(true); setBulkNotice(null);
    try {
      const res = await platformApi.bulkTenants({ ids: sel.selectedIds.map(Number), action, ...extra }) as AnyRecord;
      const failed = Number(res.failed ?? 0);
      setBulkNotice(
        `${action.replace(/-/g, " ")}: ${res.succeeded}/${res.requested} applied${failed > 0 ? ` · ${failed} failed` : ""}`,
      );
      sel.clear();
      refresh();
    } catch (e) {
      setBulkNotice(e instanceof Error ? e.message : "Bulk action failed");
    } finally {
      setBulkBusy(false); setBulkConfirm(null);
    }
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Tenant Management"
        title="Tenants"
        description="Provision accounts, assign packages, control subscription status and feature entitlements."
        actions={canManage ? <PButton onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New Tenant</PButton> : undefined}
      />

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-64 flex-1 max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, code, package…"
            className="w-full rounded-[14px] border border-slate-300 bg-white py-2.5 pl-9 pr-3 text-sm text-slate-900 placeholder:text-slate-400 outline-none shadow-sm focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          aria-label="Filter by subscription status"
          className="rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none shadow-sm focus:border-teal-400"
        >
          <option value="">All statuses</option>
          {["trial", "active", "past_due", "suspended", "cancelled", "manual_contract"].map((s) => (
            <option key={s} value={s}>{s.replace(/_/g, " ")}</option>
          ))}
        </select>
      </div>

      {bulkNotice && (
        <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{bulkNotice}</div>
      )}

      {rows.length === 0 ? (
        all.length === 0
          ? <PEmpty title="No tenants yet" subtitle="Create your first tenant to start provisioning the SaaS business." />
          : <PEmpty title="No tenants match" subtitle="Adjust the search or status filter." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-left text-sm">
              <thead className="border-b border-slate-800 bg-slate-900/80">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {canManage && (
                    <th className="w-10 px-5 py-3">
                      <PCheckbox
                        checked={sel.allVisibleSelected}
                        indeterminate={sel.someVisibleSelected}
                        onToggle={() => sel.toggleAllVisible()}
                        ariaLabel="Select all tenants"
                      />
                    </th>
                  )}
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
                    className={`cursor-pointer transition hover:bg-slate-800/40 ${sel.isSelected(t.id) ? "bg-teal-500/5" : ""}`}
                  >
                    {canManage && (
                      <td className="px-5 py-3.5" onClick={(e) => e.stopPropagation()}>
                        <PCheckbox
                          checked={sel.isSelected(t.id)}
                          onToggle={(shift) => sel.toggle(t.id, shift)}
                          ariaLabel={`Select ${String(t.name)}`}
                        />
                      </td>
                    )}
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

      {canManage && (
        <PBulkBar count={sel.count} onClear={sel.clear}>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("activate")}>Activate</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("extend-trial", { days: 14 })}>Extend trial +14d</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("revoke-sessions")}>Revoke sessions</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => setBulkConfirm("suspend")}>Suspend</PButton>
          <PButton variant="danger" disabled={bulkBusy} onClick={() => setBulkConfirm("cancel")}>Cancel</PButton>
          {canOffboard && (
            <PButton variant="danger" disabled={bulkBusy} onClick={() => setBulkConfirm("delete")}>Delete</PButton>
          )}
        </PBulkBar>
      )}

      <PConfirm
        open={bulkConfirm === "suspend"}
        title={`Suspend ${sel.count} tenant${sel.count === 1 ? "" : "s"}?`}
        body={<>All users of the selected tenants will be locked out immediately and every active session revoked. You can reactivate at any time.</>}
        confirmLabel="Suspend tenants"
        busy={bulkBusy}
        onConfirm={() => runBulk("suspend")}
        onClose={() => setBulkConfirm(null)}
      />
      <PConfirm
        open={bulkConfirm === "cancel"}
        title={`Cancel ${sel.count} tenant subscription${sel.count === 1 ? "" : "s"}?`}
        body={<>Cancelling locks out all users and revokes every session for the selected tenants. Tenant data is retained. Reactivation requires platform action.</>}
        confirmLabel="Cancel subscriptions"
        busy={bulkBusy}
        onConfirm={() => runBulk("cancel")}
        onClose={() => setBulkConfirm(null)}
      />
      <PConfirm
        open={bulkConfirm === "delete"}
        title={`Permanently delete ${sel.count} tenant${sel.count === 1 ? "" : "s"}?`}
        body={<>This purges ALL data for the selected tenants — every job, vehicle, driver, user and record — and cannot be undone. Type <strong>DELETE</strong> to confirm.</>}
        confirmLabel={`Delete ${sel.count} tenant${sel.count === 1 ? "" : "s"}`}
        confirmText="DELETE"
        busy={bulkBusy}
        onConfirm={() => runBulk("delete", { confirm: "DELETE" })}
        onClose={() => setBulkConfirm(null)}
      />

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
          canOffboard={canOffboard}
          canEntitlements={canEntitlements}
          gatedModules={GATED_MODULES}
          onClose={() => setSelectedId(null)}
          onChanged={refresh}
        />
      )}
    </div>
  );
}

const EMPTY_TENANT_FORM = {
  name: "", legalName: "", industry: "Logistics", website: "", fleetSize: "",
  countryCode: "", taxId: "",
  primaryContactName: "", primaryContactEmail: "", primaryContactPhone: "",
  packageId: "", seatLimit: "5", billingCurrency: "", billingCycle: "monthly", trialDays: "14",
  contractStart: "", contractEnd: "",
  accountOwner: "", supportOwner: "",
  billingEmail: "", adminEmail: "",
};

function DrawerSection({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section className="space-y-3 border-t border-slate-200 pt-4 first:border-t-0 first:pt-0">
      <div>
        <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{title}</h3>
        {hint && <p className="mt-0.5 text-[11px] text-slate-400">{hint}</p>}
      </div>
      {children}
    </section>
  );
}

function isEmail(v: string) { return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v); }

function CreateTenantDrawer({ packages, onClose, onCreated }: {
  packages: AnyRecord[]; onClose: () => void; onCreated: () => void;
}) {
  const [form, setForm] = useState(EMPTY_TENANT_FORM);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const set = (patch: Partial<typeof EMPTY_TENANT_FORM>) => setForm((f) => ({ ...f, ...patch }));

  // Country list is server-driven (never hardcoded) so new countries appear here
  // as soon as they are added through the platform country-profile CRUD.
  const { data: countryProfiles } = useQuery({ queryKey: ["platform", "country-profiles"], queryFn: platformApi.countryProfiles });
  const countries = (countryProfiles ?? []) as AnyRecord[];
  const selectedCountry = countries.find((c) => String(c.countryCode) === form.countryCode);
  const autoFeatures = ((selectedCountry?.autoEnabledFeatures ?? []) as unknown[]).map(String);
  const taxLabel = selectedCountry ? String(selectedCountry.taxIdLabel ?? "Tax ID") : "Tax / VAT ID";
  const inheritedCurrency = String(selectedCountry?.defaultCurrency ?? "USD");

  // Validation gates the submit button and surfaces the first blocking reason.
  const emailInvalid =
    (form.primaryContactEmail && !isEmail(form.primaryContactEmail)) ||
    (form.billingEmail && !isEmail(form.billingEmail)) ||
    (form.adminEmail && !isEmail(form.adminEmail));
  const datesInvalid = Boolean(form.contractStart && form.contractEnd && form.contractStart > form.contractEnd);
  const canSubmit = Boolean(form.name.trim()) && !emailInvalid && !datesInvalid && Number(form.seatLimit) >= 1;

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      await platformApi.createTenant({
        name: form.name.trim(),
        legalName: form.legalName || undefined,
        industry: form.industry || undefined,
        website: form.website || undefined,
        fleetSize: form.fleetSize ? Number(form.fleetSize) : undefined,
        countryCode: form.countryCode || undefined,
        taxId: form.taxId || undefined,
        primaryContactName: form.primaryContactName || undefined,
        primaryContactEmail: form.primaryContactEmail || undefined,
        primaryContactPhone: form.primaryContactPhone || undefined,
        packageId: form.packageId ? Number(form.packageId) : undefined,
        seatLimit: Number(form.seatLimit),
        billingCurrency: form.billingCurrency || undefined,
        billingCycle: form.billingCycle,
        trialDays: Number(form.trialDays),
        contractStart: form.contractStart || undefined,
        contractEnd: form.contractEnd || undefined,
        accountOwner: form.accountOwner || undefined,
        supportOwner: form.supportOwner || undefined,
        billingEmail: form.billingEmail || undefined,
        adminEmail: form.adminEmail || undefined,
        status: "trial",
      });
      onCreated();
    } catch (e) {
      setErr(e instanceof Error ? e.message : "Failed to create tenant");
    } finally { setBusy(false); }
  };

  return (
    <PDrawer open onClose={onClose} title="New Tenant">
      {err && <div className="mb-4 rounded-xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-700">{err}</div>}
      <div className="space-y-5">
        <DrawerSection title="Company">
          <PField label="Company name *"><PInput value={form.name} onChange={(e) => set({ name: e.target.value })} placeholder="Acme Logistics" /></PField>
          <PField label="Legal entity name"><PInput value={form.legalName} onChange={(e) => set({ legalName: e.target.value })} placeholder="Acme Logistics LLC" /></PField>
          <div className="grid grid-cols-2 gap-3">
            <PField label="Industry"><PInput value={form.industry} onChange={(e) => set({ industry: e.target.value })} /></PField>
            <PField label="Fleet size (vehicles)"><PInput type="number" min={0} value={form.fleetSize} onChange={(e) => set({ fleetSize: e.target.value })} placeholder="e.g. 120" /></PField>
          </div>
          <PField label="Website"><PInput value={form.website} onChange={(e) => set({ website: e.target.value })} placeholder="https://acme.com" /></PField>
        </DrawerSection>

        <DrawerSection title="Operating region" hint="Applies the country cascade on creation: default currency, locale, timezone, calendar and country-gated modules.">
          <PField label="Country">
            <PSelect value={form.countryCode} onChange={(e) => set({ countryCode: e.target.value })}>
              <option value="">— None (defaults: USD, no auto-enabled features) —</option>
              {countries.map((c) => (
                <option key={String(c.countryCode)} value={String(c.countryCode)}>
                  {String(c.countryName)} ({String(c.countryCode)})
                </option>
              ))}
            </PSelect>
          </PField>
          <PField label={taxLabel}><PInput value={form.taxId} onChange={(e) => set({ taxId: e.target.value })} placeholder={selectedCountry ? `${taxLabel}…` : "Tax / VAT ID"} /></PField>

          {selectedCountry && (
            <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-3 text-sm">
              <p className="mb-2 text-xs font-bold uppercase tracking-wider text-teal-700">On creation, this country will apply</p>
              <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-slate-600">
                <span className="text-slate-400">Currency</span><span className="font-medium text-slate-800">{String(selectedCountry.defaultCurrency)}</span>
                <span className="text-slate-400">Locale</span><span className="font-medium text-slate-800">{String(selectedCountry.defaultLocale)}</span>
                <span className="text-slate-400">Text direction</span><span className="font-medium text-slate-800 uppercase">{String(selectedCountry.textDirection)}</span>
                <span className="text-slate-400">Calendar</span><span className="font-medium text-slate-800">{String(selectedCountry.calendarSystem)}</span>
                <span className="text-slate-400">Invoicing</span><span className="font-medium text-slate-800">{String(selectedCountry.invoicingScheme)}</span>
              </div>
              <p className="mt-3 mb-1.5 text-xs text-slate-500">
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
        </DrawerSection>

        <DrawerSection title="Primary contact">
          <PField label="Full name"><PInput value={form.primaryContactName} onChange={(e) => set({ primaryContactName: e.target.value })} placeholder="Jane Doe" /></PField>
          <div className="grid grid-cols-2 gap-3">
            <PField label="Email"><PInput type="email" value={form.primaryContactEmail} onChange={(e) => set({ primaryContactEmail: e.target.value })} placeholder="jane@acme.com" /></PField>
            <PField label="Phone"><PInput value={form.primaryContactPhone} onChange={(e) => set({ primaryContactPhone: e.target.value })} placeholder="+1 555 012 3456" /></PField>
          </div>
        </DrawerSection>

        <DrawerSection title="Subscription & commercial terms">
          <PField label="Package">
            <PSelect value={form.packageId} onChange={(e) => set({ packageId: e.target.value })}>
              <option value="">— None (trial, no package) —</option>
              {packages.map((p) => <option key={String(p.id)} value={String(p.id)}>{String(p.name)}</option>)}
            </PSelect>
          </PField>
          <div className="grid grid-cols-3 gap-3">
            <PField label="Seat limit"><PInput type="number" min={1} value={form.seatLimit} onChange={(e) => set({ seatLimit: e.target.value })} /></PField>
            <PField label="Billing cycle">
              <PSelect value={form.billingCycle} onChange={(e) => set({ billingCycle: e.target.value })}>
                <option value="monthly">Monthly</option>
                <option value="annual">Annual</option>
              </PSelect>
            </PField>
            <PField label="Trial days"><PInput type="number" min={0} value={form.trialDays} onChange={(e) => set({ trialDays: e.target.value })} /></PField>
          </div>
          <PField label={`Billing currency (blank = inherit ${inheritedCurrency})`}>
            <PInput value={form.billingCurrency} onChange={(e) => set({ billingCurrency: e.target.value.toUpperCase() })} maxLength={3} placeholder={inheritedCurrency} />
          </PField>
          <div className="grid grid-cols-2 gap-3">
            <PField label="Contract start"><PInput type="date" value={form.contractStart} onChange={(e) => set({ contractStart: e.target.value })} /></PField>
            <PField label="Contract end"><PInput type="date" value={form.contractEnd} onChange={(e) => set({ contractEnd: e.target.value })} /></PField>
          </div>
          {datesInvalid && <p className="text-[11px] text-red-600">Contract end must be on or after contract start.</p>}
        </DrawerSection>

        <DrawerSection title="Ownership & billing">
          <div className="grid grid-cols-2 gap-3">
            <PField label="Account owner (CSM)"><PInput value={form.accountOwner} onChange={(e) => set({ accountOwner: e.target.value })} placeholder="owner@opstrax.com" /></PField>
            <PField label="Support owner"><PInput value={form.supportOwner} onChange={(e) => set({ supportOwner: e.target.value })} placeholder="support@opstrax.com" /></PField>
          </div>
          <PField label="Billing email"><PInput type="email" value={form.billingEmail} onChange={(e) => set({ billingEmail: e.target.value })} placeholder="ap@acme.com" /></PField>
          <PField label="Tenant admin email (invite)"><PInput type="email" value={form.adminEmail} onChange={(e) => set({ adminEmail: e.target.value })} placeholder="admin@acme.com" /></PField>
        </DrawerSection>

        {emailInvalid && <p className="text-[11px] text-red-600">One or more email addresses are invalid.</p>}
        <div className="flex gap-2 pt-1">
          <PButton onClick={submit} disabled={busy || !canSubmit}>Create tenant</PButton>
          <PButton variant="ghost" onClick={onClose}>Cancel</PButton>
        </div>
      </div>
    </PDrawer>
  );
}

function TenantDetailDrawer({ id, packages, canManage, canOffboard, canEntitlements, gatedModules, onClose, onChanged }: {
  id: number; packages: AnyRecord[]; canManage: boolean; canOffboard: boolean; canEntitlements: boolean; gatedModules: string[];
  onClose: () => void; onChanged: () => void;
}) {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["platform", "tenant", id], queryFn: () => platformApi.tenant(id) });
  const [busy, setBusy] = useState(false);
  const [assignPkg, setAssignPkg] = useState("");
  const [confirm, setConfirm] = useState<"suspend" | "cancel" | "revoke" | "delete" | null>(null);
  const [seatEdit, setSeatEdit] = useState("");
  const [regionEdit, setRegionEdit] = useState("");
  const [inviteEmail, setInviteEmail] = useState("");
  const [notice, setNotice] = useState<string | null>(null);

  // Server-driven country list (shared cache with the create drawer). Changing a
  // tenant's region re-runs the country cascade: currency/timezone defaults plus
  // country-gated modules (e.g. Saudi Readiness) follow the reassignment.
  const { data: countryProfiles } = useQuery({ queryKey: ["platform", "country-profiles"], queryFn: platformApi.countryProfiles });
  const countryOptions = (countryProfiles ?? []) as AnyRecord[];

  const reload = () => { qc.invalidateQueries({ queryKey: ["platform", "tenant", id] }); onChanged(); };

  const act = async (fn: () => Promise<unknown>, done?: string) => {
    setBusy(true); setNotice(null);
    try { await fn(); reload(); if (done) setNotice(done); }
    catch (e) { setNotice(e instanceof Error ? e.message : "Action failed"); }
    finally { setBusy(false); setConfirm(null); }
  };

  const tenant = (data?.tenant ?? {}) as AnyRecord;
  const entitlements = (data?.entitlements ?? []) as AnyRecord[];
  const entMap = new Map(entitlements.map((e) => [String(e.moduleKey), e]));
  const tenantCode = String(tenant.companyCode ?? "");

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
            <Info label="Operating region" value={tenant.country ? `${String(tenant.country)} · ${String(tenant.currency ?? "")}` : "Not set"} />
            <Info label="Account owner" value={String(tenant.accountOwner ?? "—")} />
            <Info label="Support owner" value={String(tenant.supportOwner ?? "—")} />
            <Info label="Fleet size" value={tenant.fleetSize != null ? `${String(tenant.fleetSize)} vehicles` : "—"} />
            <Info label="Billing cycle" value={String(tenant.billingCycle ?? "—")} />
            <Info label="Primary contact" value={String(tenant.primaryContactName ?? "—")} />
            <Info label="Contact email" value={String(tenant.primaryContactEmail ?? "—")} />
            <Info label="Billing email" value={String(tenant.billingEmail ?? "—")} />
            <Info label="Tax / VAT ID" value={String(tenant.taxId ?? "—")} />
            <Info label="Trial ends" value={String(tenant.trialEndsAt ?? "—").slice(0, 10) || "—"} />
            <Info label="Contract end" value={String(tenant.contractEnd ?? "—").slice(0, 10) || "—"} />
          </div>

          {notice && (
            <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{notice}</div>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Subscription actions</h3>
              <div className="flex flex-wrap gap-2">
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "activate" }), "Tenant activated")}>Activate</PButton>
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "extend-trial", days: 14 }), "Trial extended 14 days")}>Extend trial +14d</PButton>
                <PButton variant="ghost" disabled={busy} onClick={() => setConfirm("suspend")}>Suspend</PButton>
                <PButton variant="danger" disabled={busy} onClick={() => setConfirm("cancel")}>Cancel</PButton>
              </div>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Limits</h3>
              <div className="flex gap-2">
                <PInput
                  type="number"
                  min={1}
                  value={seatEdit}
                  onChange={(e) => setSeatEdit(e.target.value)}
                  placeholder={`Seat limit (current: ${String(tenant.seatLimit ?? "—")})`}
                  aria-label="Seat limit"
                />
                <PButton
                  disabled={busy || !seatEdit || Number(seatEdit) < 1}
                  onClick={() => act(() => platformApi.updateTenant(id, { seatLimit: Number(seatEdit) }), "Seat limit updated")}
                >
                  Save
                </PButton>
              </div>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Operating region</h3>
              <div className="flex gap-2">
                <PSelect value={regionEdit || String(tenant.country ?? "")} onChange={(e) => setRegionEdit(e.target.value)} aria-label="Operating region">
                  <option value="">— Not set —</option>
                  {countryOptions.map((c) => (
                    <option key={String(c.countryCode)} value={String(c.countryCode)}>
                      {String(c.countryName)} ({String(c.countryCode)})
                    </option>
                  ))}
                </PSelect>
                <PButton
                  disabled={busy || !regionEdit || regionEdit === String(tenant.country ?? "")}
                  onClick={() => act(() => platformApi.updateTenant(id, { countryCode: regionEdit }), "Operating region updated — tenant users see region modules after next login")}
                >
                  Save
                </PButton>
              </div>
              <p className="mt-1.5 text-[11px] text-slate-500">
                Applies the country profile cascade: default currency, timezone and country
                features. Region-scoped modules (e.g. Saudi Readiness) follow this setting.
              </p>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Tenant admin & sessions</h3>
              <div className="flex gap-2">
                <PInput
                  type="email"
                  value={inviteEmail}
                  onChange={(e) => setInviteEmail(e.target.value)}
                  placeholder="admin@tenant.com"
                  aria-label="Tenant admin email"
                />
                <PButton
                  disabled={busy || !inviteEmail.includes("@")}
                  onClick={() => act(() => platformApi.resetInvite(id, { adminEmail: inviteEmail }), "Admin invite sent/reset")}
                >
                  Invite / Reset admin
                </PButton>
              </div>
              <div className="mt-2">
                <PButton variant="ghost" disabled={busy} onClick={() => setConfirm("revoke")}>
                  Revoke all tenant sessions
                </PButton>
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

          {canOffboard && (
            <section className="rounded-xl border border-red-200 bg-red-50/60 p-4">
              <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-red-600">Danger zone</h3>
              <p className="mb-3 text-xs text-red-600/90">
                Permanently deletes this tenant and ALL its data (jobs, vehicles, drivers, users, records). Irreversible.
              </p>
              <PButton variant="danger" disabled={busy} onClick={() => setConfirm("delete")}>Delete tenant permanently</PButton>
            </section>
          )}

          <PConfirm
            open={confirm === "delete"}
            title="Permanently delete this tenant?"
            body={<>This purges <strong>{String(tenant.name ?? "this tenant")}</strong> and every record it owns. This cannot be undone. Type the tenant code to confirm.</>}
            confirmLabel="Delete tenant"
            confirmText={tenantCode || undefined}
            busy={busy}
            onConfirm={() => act(async () => { await platformApi.deleteTenant(id, tenantCode); onClose(); }, "Tenant deleted")}
            onClose={() => setConfirm(null)}
          />
          <PConfirm
            open={confirm === "suspend"}
            title="Suspend this tenant?"
            body={<>All users of <strong>{String(tenant.name ?? "this tenant")}</strong> will be locked out immediately and every active session revoked. You can reactivate at any time.</>}
            confirmLabel="Suspend tenant"
            busy={busy}
            onConfirm={() => act(() => platformApi.tenantStatus(id, { action: "suspend" }), "Tenant suspended — sessions revoked")}
            onClose={() => setConfirm(null)}
          />
          <PConfirm
            open={confirm === "cancel"}
            title="Cancel this tenant's subscription?"
            body={<>Cancelling locks out all users and revokes every session. Tenant data is retained. This is a commercial off-switch — reactivation requires platform action.</>}
            confirmLabel="Cancel subscription"
            confirmText={tenantCode || undefined}
            busy={busy}
            onConfirm={() => act(() => platformApi.tenantStatus(id, { action: "cancel" }), "Tenant cancelled — sessions revoked")}
            onClose={() => setConfirm(null)}
          />
          <PConfirm
            open={confirm === "revoke"}
            title="Revoke all tenant sessions?"
            body={<>Every logged-in user of <strong>{String(tenant.name ?? "this tenant")}</strong> is signed out immediately. The subscription status does not change.</>}
            confirmLabel="Revoke sessions"
            busy={busy}
            onConfirm={() => act(() => platformApi.revokeSessions(id), "All tenant sessions revoked")}
            onClose={() => setConfirm(null)}
          />
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
