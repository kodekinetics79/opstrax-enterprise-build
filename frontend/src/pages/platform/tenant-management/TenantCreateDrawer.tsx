import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { PBadge, PButton, PDrawer, PField, PInput, PSelect } from "../ui";

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

export function CreateTenantDrawer({ packages, onClose, onCreated }: {
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
        entitlementPolicyMode: "package_allowlist",
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
          <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-3 text-xs leading-5 text-teal-900/80">
            <strong className="font-semibold text-teal-950">Package allowlist policy:</strong> this new tenant starts deny-by-default. Only modules in the selected package, country grants, or explicit Platform overrides are available.
          </div>
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
