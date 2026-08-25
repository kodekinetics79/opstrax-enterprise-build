import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Building2, Search } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney, formatAmount, minorUnits } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import {
  PHeader, PCard, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty, PDrawer, PConfirm,
  PCheckbox, PBulkBar, useRowSelection,
} from "./ui";

// The modules a Platform Admin can turn on/off per tenant. `key` must match the
// module_key the API gate uses (Program.cs ModuleKeyForPath) — the blurb spells out
// exactly which API surface each toggle governs, so the control is self-explanatory.
type GatedModule = { key: string; label: string; blurb: string };

const GATED_MODULES: GatedModule[] = [
  { key: "safety",          label: "Safety",          blurb: "Safety events, dashcam, incidents, coaching, traffic violations" },
  { key: "maintenance",     label: "Maintenance",     blurb: "Work orders, PM schedules, DVIR, service history, downtime" },
  { key: "dispatch",        label: "Dispatch",        blurb: "Dispatch board, jobs, trips, routes, smart assign, last mile" },
  { key: "telematics",      label: "Telematics",      blurb: "Live telemetry, devices, ELD, geofences" },
  { key: "crm",             label: "CRM",             blurb: "Customers, contracts, leads, opportunities, quotations, rate cards" },
  { key: "customer_portal", label: "Customer Portal", blurb: "Customer-facing portal, ETA sharing, shipment visibility" },
  { key: "reports",         label: "Reports",         blurb: "Reporting and analytics" },
  { key: "compliance",      label: "Compliance",      blurb: "Compliance rules, HOS, audit packages" },
  { key: "integrations",    label: "Integrations",    blurb: "Third-party connectors, provider sync, tests and external actions" },
];

// The one-time activation artifact for an invited tenant administrator. Shown whenever an
// invite is issued: when SMTP delivered it this is the backup copy, and when it did not, this
// is the ONLY way the invited person can ever set a password. Previously the API returned
// neither the link nor the token, so an invite with email unconfigured left the user Pending
// with nothing to send them.
function ActivationLinkPanel({ email, url, token, emailSent, onDismiss }: {
  email: string; url?: string; token?: string; emailSent: boolean; onDismiss: () => void;
}) {
  const [copied, setCopied] = useState(false);
  if (!url && !token) return null;
  return (
    <div className={`rounded-[14px] border p-4 ${emailSent ? "border-emerald-200 bg-emerald-50" : "border-amber-200 bg-amber-50"}`}>
      <p className={`text-sm font-bold ${emailSent ? "text-emerald-800" : "text-amber-800"}`}>
        {emailSent ? `Activation email sent to ${email}` : "Activation link — deliver this manually"}
      </p>
      <p className={`mt-1 text-xs leading-5 ${emailSent ? "text-emerald-700" : "text-amber-700"}`}>
        {emailSent
          ? "The link below is your backup copy. It is valid for 7 days and is shown only once."
          : `Email is not configured, so nothing was sent to ${email}. Give them this link — it is valid for 7 days and is shown only once.`}
      </p>
      {url ? (
        <div className="mt-3 flex items-center gap-2">
          <code className="min-w-0 flex-1 truncate rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs text-slate-700">{url}</code>
          <PButton variant="ghost" onClick={() => { void navigator.clipboard.writeText(url).then(() => setCopied(true)); }}>
            {copied ? "Copied" : "Copy"}
          </PButton>
        </div>
      ) : (
        <p className="mt-3 text-xs leading-5 text-amber-800">
          No tenant application URL is configured, so a full link cannot be built. Set one under
          Email &amp; SMTP → Application URLs, then re-issue the invite.
        </p>
      )}
      <div className="mt-3 flex justify-end">
        <PButton variant="ghost" onClick={onDismiss}>Dismiss</PButton>
      </div>
    </div>
  );
}

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

  const pkgs = (packages ?? []) as AnyRecord[];
  const sel = useRowSelection(rows.map((t) => Number(t.id)));
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkConfirm, setBulkConfirm] = useState<"suspend" | "cancel" | "delete" | null>(null);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);
  const [bulkPkg, setBulkPkg] = useState("");

  // R (bulk read/export): dump the selected tenants — the live, company-scoped rows
  // already rendered in the table — to a CSV the operator can open in a spreadsheet.
  // Pure client-side over data the /api/platform/tenants read returned; no new fetch.
  const exportSelected = () => {
    const chosen = new Set(sel.selectedIds.map(String));
    const picked = rows.filter((t) => chosen.has(String(t.id)));
    if (picked.length === 0) return;
    const cols: [string, string][] = [
      ["id", "id"], ["name", "name"], ["companyCode", "code"], ["status", "status"],
      ["packageName", "package"], ["seatLimit", "seat_limit"], ["mrrCents", "mrr_cents"],
      ["userCount", "users"], ["country", "country"], ["currency", "currency"],
      ["industry", "industry"], ["primaryContactEmail", "primary_contact_email"],
      ["billingEmail", "billing_email"], ["createdAt", "created_at"],
    ];
    const esc = (v: unknown) => {
      const s = v == null ? "" : String(v);
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const csv = [
      cols.map(([, h]) => h).join(","),
      ...picked.map((t) => cols.map(([k]) => esc(t[k])).join(",")),
    ].join("\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `tenants-export-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    setBulkNotice(`Exported ${picked.length} tenant${picked.length === 1 ? "" : "s"} to CSV`);
  };

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
          <PButton variant="ghost" disabled={bulkBusy} onClick={exportSelected}>Export CSV</PButton>
          <div className="flex items-center gap-1.5">
            <PSelect value={bulkPkg} onChange={(e) => setBulkPkg(e.target.value)} aria-label="Assign package to selected tenants">
              <option value="">Assign package…</option>
              {pkgs.map((p) => <option key={String(p.id)} value={String(p.id)}>{String(p.name)}</option>)}
            </PSelect>
            <PButton
              variant="ghost"
              disabled={bulkBusy || !bulkPkg}
              onClick={() => runBulk("assign-package", { packageId: Number(bulkPkg) }).then(() => setBulkPkg(""))}
            >
              Assign
            </PButton>
          </div>
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
          <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-3 text-xs leading-5 text-slate-600">
            <strong className="font-semibold text-slate-800">Package allowlist policy:</strong> this new tenant starts deny-by-default. Only modules in the selected package, country grants, or explicit Platform overrides are available.
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

function TenantDetailDrawer({ id, packages, canManage, canOffboard, canEntitlements, gatedModules, onClose, onChanged }: {
  id: number; packages: AnyRecord[]; canManage: boolean; canOffboard: boolean; canEntitlements: boolean; gatedModules: GatedModule[];
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
  const [snapshotBusy, setSnapshotBusy] = useState(false);
  const snapshotBaselineKey = `opstrax:platform:control-snapshot-baseline:${id}`;
  const [snapshotBaseline, setSnapshotBaseline] = useState<{ semanticSha256: string; snapshotSha256: string } | null>(() => {
    try {
      const raw = sessionStorage.getItem(snapshotBaselineKey);
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  });
  useEffect(() => {
    try {
      const raw = sessionStorage.getItem(snapshotBaselineKey);
      setSnapshotBaseline(raw ? JSON.parse(raw) : null);
    } catch { setSnapshotBaseline(null); }
  }, [snapshotBaselineKey]);
  const [policyChange, setPolicyChange] = useState<"legacy_allow" | "package_allowlist" | null>(null);

  // Server-driven country list (shared cache with the create drawer). Changing a
  // tenant's region re-runs the country cascade: currency/timezone defaults plus
  // country-gated modules (e.g. Saudi Readiness) follow the reassignment.
  const { data: countryProfiles } = useQuery({ queryKey: ["platform", "country-profiles"], queryFn: platformApi.countryProfiles });
  const countryOptions = (countryProfiles ?? []) as AnyRecord[];

  const reload = () => { qc.invalidateQueries({ queryKey: ["platform", "tenant", id] }); onChanged(); };

  // Full company-profile edit form, hydrated once the tenant loads.
  const [edit, setEdit] = useState<Record<string, string>>({});
  useEffect(() => {
    const t = data?.tenant as AnyRecord | undefined;
    if (!t) return;
    setEdit({
      name: String(t.name ?? ""),
      legalName: String(t.legalName ?? ""),
      industry: String(t.industry ?? ""),
      website: String(t.website ?? ""),
      fleetSize: t.fleetSize != null ? String(t.fleetSize) : "",
      taxId: String(t.taxId ?? ""),
      primaryContactName: String(t.primaryContactName ?? ""),
      primaryContactEmail: String(t.primaryContactEmail ?? ""),
      primaryContactPhone: String(t.primaryContactPhone ?? ""),
      billingEmail: String(t.billingEmail ?? ""),
      billingCycle: String(t.billingCycle ?? "monthly"),
    });
  }, [data]);
  const setF = (k: string, v: string) => setEdit((f) => ({ ...f, [k]: v }));

  // Tenant user directory + platform-initiated password reset.
  const usersQ = useQuery({ queryKey: ["platform", "tenant", id, "users"], queryFn: () => platformApi.tenantUsers(id) });
  const tenantUsers = ((usersQ.data as AnyRecord)?.users ?? []) as AnyRecord[];
  const assignableRoles = ((usersQ.data as AnyRecord)?.roles ?? []) as AnyRecord[];
  const reloadUsers = () => qc.invalidateQueries({ queryKey: ["platform", "tenant", id, "users"] });
  const [resetBusyId, setResetBusyId] = useState<number | null>(null);
  const [tempPw, setTempPw] = useState<AnyRecord | null>(null);
  const [copied, setCopied] = useState(false);
  const [editingUserId, setEditingUserId] = useState<number | null>(null);
  const [addUserOpen, setAddUserOpen] = useState(false);

  const resetUserPassword = async (userId: number) => {
    setResetBusyId(userId); setNotice(null); setTempPw(null); setCopied(false);
    try {
      setTempPw(await platformApi.resetTenantUserPassword(id, userId));
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Password reset failed");
    } finally { setResetBusyId(null); }
  };

  // Holds the one-time activation artifact returned by the last invite so it can be copied.
  // Cleared explicitly by the operator, never on reload — losing it would mean re-issuing the
  // invite just to get the link back.
  const [issuedInvite, setIssuedInvite] = useState<
    { email: string; url?: string; token?: string; emailSent: boolean } | null>(null);

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
  const policyMode = String(tenant.entitlementPolicyMode ?? "legacy_allow") as "legacy_allow" | "package_allowlist";

  const captureControlSnapshot = async () => {
    setSnapshotBusy(true); setNotice(null);
    try {
      const evidence = await platformApi.captureTenantControlSnapshot(id);
      const blob = new Blob([JSON.stringify(evidence, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `${tenantCode || `tenant-${id}`}-control-snapshot-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;
      anchor.click();
      URL.revokeObjectURL(url);
      const snapshot = (evidence.snapshot ?? {}) as AnyRecord;
      const semanticComparison = (snapshot.semanticComparison ?? {}) as AnyRecord;
      const current = {
        semanticSha256: String(evidence.semanticSha256 ?? semanticComparison.semanticSha256 ?? ""),
        snapshotSha256: String(evidence.snapshotSha256 ?? "unavailable"),
      };
      if (!current.semanticSha256) {
        setNotice(`Audited control snapshot captured · SHA-256 ${current.snapshotSha256} · semantic comparison unavailable`);
      } else if (!snapshotBaseline) {
        sessionStorage.setItem(snapshotBaselineKey, JSON.stringify(current));
        setSnapshotBaseline(current);
        setNotice(`Audited baseline captured · evidence SHA-256 ${current.snapshotSha256} · semantic SHA-256 ${current.semanticSha256}`);
      } else {
        const unchanged = snapshotBaseline.semanticSha256 === current.semanticSha256;
        setNotice(
          `${unchanged ? "No semantic control drift" : "Semantic control drift detected"} · ` +
          `baseline ${snapshotBaseline.semanticSha256} · current ${current.semanticSha256} · evidence ${current.snapshotSha256}`,
        );
      }
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Control snapshot capture failed");
    } finally { setSnapshotBusy(false); }
  };

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
            <Info label="Access policy" value={policyMode === "package_allowlist" ? "Package allowlist" : "Legacy allow"} />
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

          <section className="rounded-xl border border-teal-200 bg-teal-50/60 p-4">
            <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-teal-700">Release control evidence</h3>
            <p className="mb-3 text-xs leading-5 text-teal-800/80">
              Capture a server-generated, SHA-256 identified JSON snapshot of lifecycle, package policy,
              effective entitlements, role grants, pseudonymous user-to-branch bindings, market packs,
              flags, connector readiness, environment posture and recent Platform audit IDs. Secrets
              and actor/user PII are excluded. Repeat capture compares semantic controls while ignoring
              timestamps and audit-capture drift.
            </p>
            <div className="flex flex-wrap gap-2">
              <PButton variant="ghost" disabled={snapshotBusy} onClick={captureControlSnapshot}>
                {snapshotBusy ? "Capturing…" : snapshotBaseline ? "Capture and compare snapshot" : "Capture audited control snapshot"}
              </PButton>
              {snapshotBaseline && (
                <PButton variant="ghost" disabled={snapshotBusy} onClick={() => {
                  sessionStorage.removeItem(snapshotBaselineKey);
                  setSnapshotBaseline(null);
                  setNotice("Snapshot comparison baseline cleared; the next capture becomes the baseline.");
                }}>
                  Clear comparison baseline
                </PButton>
              )}
            </div>
          </section>

          {notice && (
            <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{notice}</div>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Tenant details</h3>
              <div className="space-y-3">
                <PField label="Company name"><PInput value={edit.name ?? ""} onChange={(e) => setF("name", e.target.value)} /></PField>
                <PField label="Legal entity name"><PInput value={edit.legalName ?? ""} onChange={(e) => setF("legalName", e.target.value)} /></PField>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Industry"><PInput value={edit.industry ?? ""} onChange={(e) => setF("industry", e.target.value)} /></PField>
                  <PField label="Fleet size"><PInput type="number" min={0} value={edit.fleetSize ?? ""} onChange={(e) => setF("fleetSize", e.target.value)} /></PField>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Website"><PInput value={edit.website ?? ""} onChange={(e) => setF("website", e.target.value)} /></PField>
                  <PField label="Tax / VAT ID"><PInput value={edit.taxId ?? ""} onChange={(e) => setF("taxId", e.target.value)} /></PField>
                </div>
                <PField label="Primary contact"><PInput value={edit.primaryContactName ?? ""} onChange={(e) => setF("primaryContactName", e.target.value)} /></PField>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Contact email"><PInput type="email" value={edit.primaryContactEmail ?? ""} onChange={(e) => setF("primaryContactEmail", e.target.value)} /></PField>
                  <PField label="Contact phone"><PInput value={edit.primaryContactPhone ?? ""} onChange={(e) => setF("primaryContactPhone", e.target.value)} /></PField>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Billing email"><PInput type="email" value={edit.billingEmail ?? ""} onChange={(e) => setF("billingEmail", e.target.value)} /></PField>
                  <PField label="Billing cycle">
                    <PSelect value={edit.billingCycle ?? "monthly"} onChange={(e) => setF("billingCycle", e.target.value)}>
                      <option value="monthly">Monthly</option>
                      <option value="annual">Annual</option>
                    </PSelect>
                  </PField>
                </div>
                <PButton
                  disabled={busy || !edit.name?.trim()}
                  onClick={() => act(() => platformApi.updateTenant(id, {
                    name: edit.name || undefined,
                    legalName: edit.legalName || undefined,
                    industry: edit.industry || undefined,
                    website: edit.website || undefined,
                    fleetSize: edit.fleetSize ? Number(edit.fleetSize) : undefined,
                    taxId: edit.taxId || undefined,
                    primaryContactName: edit.primaryContactName || undefined,
                    primaryContactEmail: edit.primaryContactEmail || undefined,
                    primaryContactPhone: edit.primaryContactPhone || undefined,
                    billingEmail: edit.billingEmail || undefined,
                    billingCycle: edit.billingCycle || undefined,
                  }), "Tenant details saved")}
                >
                  Save details
                </PButton>
              </div>
            </section>
          )}

          {/* Tenant users — full 360 administration. A platform operator can correct a
              sign-in email, change a role, disable an account, hand over a temporary
              password or re-arm an invite, without anyone inside the tenant needing to
              be able to log in first. */}
          <section>
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                Tenant users &amp; access
              </h3>
              {canManage && (
                <PButton variant="ghost" onClick={() => { setAddUserOpen((v) => !v); setEditingUserId(null); }}>
                  {addUserOpen ? "Cancel" : "Add user"}
                </PButton>
              )}
            </div>

            {tempPw && (
              <div className="mb-3 rounded-xl border border-amber-300 bg-amber-50 p-3">
                <p className="text-xs font-bold text-amber-800">Temporary password — shown only once</p>
                <p className="mt-1 text-[11px] text-amber-700">Give this to {String(tempPw.email)}. They should change it after signing in.</p>
                <div className="mt-2 flex items-center gap-2">
                  <code className="flex-1 truncate rounded-lg border border-amber-300 bg-white px-2 py-1.5 font-mono text-sm text-slate-900">{String(tempPw.temporaryPassword)}</code>
                  <PButton variant="ghost" onClick={() => { void navigator.clipboard?.writeText(String(tempPw.temporaryPassword)); setCopied(true); }}>
                    {copied ? "Copied" : "Copy"}
                  </PButton>
                </div>
              </div>
            )}

            {addUserOpen && canManage && (
              <AddTenantUserForm
                tenantId={id}
                roles={assignableRoles}
                onDone={(result, msg) => {
                  setAddUserOpen(false);
                  setTempPw(result); setCopied(false);
                  setNotice(msg);
                  reloadUsers(); reload();
                }}
              />
            )}

            {usersQ.isLoading ? <p className="text-xs text-slate-500">Loading users…</p>
              : tenantUsers.length === 0 ? <p className="text-xs text-slate-500">No users in this tenant yet.</p>
              : (
                <div className="space-y-2">
                  {tenantUsers.map((u) => (
                    <div key={String(u.id)} className="rounded-xl border border-slate-200 bg-white px-3 py-2.5">
                      <div className="flex items-center justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-medium text-slate-800">
                            {String(u.fullName ?? "—")}
                            {isAdminRole(String(u.roleName ?? "")) && (
                              <span className="ml-2 rounded bg-teal-50 px-1.5 py-0.5 text-[10px] font-bold uppercase text-teal-700">Admin</span>
                            )}
                          </p>
                          <p className="truncate text-[11px] text-slate-500">
                            {String(u.email ?? "")} · {String(u.roleName ?? "—")} · {String(u.status ?? "")}
                            {Number(u.activeSessions ?? 0) > 0 && ` · ${Number(u.activeSessions)} live session${Number(u.activeSessions) === 1 ? "" : "s"}`}
                            {u.hasPassword === false && " · no password set"}
                          </p>
                        </div>
                        {canManage && (
                          <div className="flex shrink-0 gap-1">
                            <PButton
                              variant="ghost"
                              onClick={() => { setEditingUserId(editingUserId === Number(u.id) ? null : Number(u.id)); setAddUserOpen(false); }}
                            >
                              {editingUserId === Number(u.id) ? "Close" : "Edit"}
                            </PButton>
                            <PButton variant="ghost" disabled={busy || resetBusyId === Number(u.id)}
                              onClick={() => resetUserPassword(Number(u.id))}>
                              {resetBusyId === Number(u.id) ? "Resetting…" : "Reset password"}
                            </PButton>
                          </div>
                        )}
                      </div>

                      {canManage && editingUserId === Number(u.id) && (
                        <EditTenantUserForm
                          tenantId={id}
                          user={u}
                          roles={assignableRoles}
                          onDone={(msg) => { setEditingUserId(null); setNotice(msg); reloadUsers(); reload(); }}
                          onInviteSent={(msg) => setNotice(msg)}
                        />
                      )}
                    </div>
                  ))}
                </div>
              )}
          </section>

          {/* Commercial terms — what this tenant is actually charged, feature by feature */}
          <TenantBillingPlanSection tenantId={id} canManage={canManage} onNotice={setNotice} />

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
                  onClick={() => act(async () => {
                    const res = await platformApi.resetInvite(id, { adminEmail: inviteEmail }) as AnyRecord;
                    setIssuedInvite({
                      email: inviteEmail,
                      url: res?.activationUrl ? String(res.activationUrl) : undefined,
                      token: res?.activationToken ? String(res.activationToken) : undefined,
                      emailSent: res?.emailSent === true,
                    });
                  })}
                >
                  Invite / Reset admin
                </PButton>
              </div>
              {issuedInvite && (
                <div className="mt-3">
                  <ActivationLinkPanel {...issuedInvite} onDismiss={() => setIssuedInvite(null)} />
                </div>
              )}
              <div className="mt-2">
                <PButton variant="ghost" disabled={busy} onClick={() => setConfirm("revoke")}>
                  Revoke all tenant sessions
                </PButton>
              </div>
            </section>
          )}

          {canEntitlements && (
            <section className="rounded-xl border border-amber-200 bg-amber-50/60 p-4">
              <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-amber-700">Commercial access policy</h3>
              <p className="mb-3 text-xs leading-5 text-amber-800/80">
                Package allowlist denies every governed module without an enabled entitlement. Legacy allow preserves inherited access when no row exists. Changes take effect at the API edge immediately and are audited.
              </p>
              <PSelect
                value={policyChange ?? policyMode}
                aria-label="Commercial access policy"
                onChange={(e) => setPolicyChange(e.target.value as "legacy_allow" | "package_allowlist")}
                disabled={busy}
              >
                <option value="package_allowlist">Package allowlist (deny by default)</option>
                <option value="legacy_allow">Legacy allow (compatibility)</option>
              </PSelect>
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
            <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-slate-500">Feature entitlements</h3>
            <p className="mb-3 text-xs leading-5 text-slate-500">
              Controls which product modules this tenant may use. <strong className="font-semibold text-slate-700">Server-enforced</strong> —
              turning one off makes every API route it owns return <span className="font-mono">403</span> immediately, even if called
              directly outside the UI. Missing rows follow the access policy: {policyMode === "package_allowlist"
                ? <strong className="font-semibold text-slate-700">off by default</strong>
                : <strong className="font-semibold text-slate-700">on by default</strong>}.
            </p>
            <div className="space-y-2">
              {gatedModules.map((m) => {
                const ent = entMap.get(m.key);
                const enabled = ent ? Boolean(ent.enabled) : policyMode === "legacy_allow";
                return (
                  <div key={m.key} className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-2.5">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-slate-800">{m.label}</p>
                      <p className="text-[11px] leading-4 text-slate-500">{m.blurb}</p>
                      <p className="mt-0.5 text-[11px] font-medium text-slate-400">
                        {ent ? `${String(ent.source)} · ${String(ent.tier)}` : policyMode === "legacy_allow" ? "inherited (legacy default on)" : "not in plan (default off)"}
                      </p>
                    </div>
                    <button
                      disabled={!canEntitlements || busy}
                      onClick={() => act(() => platformApi.setEntitlement(id, { moduleKey: m.key, enabled: !enabled }))}
                      className={`relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-40 ${enabled ? "bg-teal-500" : "bg-slate-300"}`}
                      aria-label={`${enabled ? "Disable" : "Enable"} ${m.label}`}
                    >
                      <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition ${enabled ? "left-[22px]" : "left-0.5"}`} />
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
            open={policyChange !== null && policyChange !== policyMode}
            title={policyChange === "package_allowlist" ? "Enable deny-by-default package access?" : "Restore legacy inherited access?"}
            body={policyChange === "package_allowlist"
              ? <>Modules outside the assigned package or explicit grants will be blocked immediately. Current package-derived rights will be reconciled.</>
              : <>Missing entitlement rows will become accessible immediately. Use this compatibility mode only for a reviewed legacy tenant.</>}
            confirmLabel={policyChange === "package_allowlist" ? "Enable package allowlist" : "Enable legacy access"}
            busy={busy}
            onConfirm={() => policyChange && act(
              () => platformApi.setEntitlementPolicy(id, policyChange),
              `Access policy changed to ${policyChange === "package_allowlist" ? "package allowlist" : "legacy allow"}`,
            ).finally(() => setPolicyChange(null))}
            onClose={() => setPolicyChange(null)}
          />
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
    <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5">
      <p className="text-[10px] uppercase tracking-wider text-slate-500">{label}</p>
      <p className="mt-0.5 text-sm font-medium text-slate-800">{value}</p>
    </div>
  );
}

// Roles carrying tenant-wide authority — mirrors PlatformEndpoints.TenantAdminRoles.
// Used only for labelling here; the server enforces the last-admin guard.
const TENANT_ADMIN_ROLES = ["company admin", "super admin", "reseller / partner admin"];

function isAdminRole(roleName: string) {
  return TENANT_ADMIN_ROLES.includes(roleName.trim().toLowerCase());
}

function AddTenantUserForm({ tenantId, roles, onDone }: {
  tenantId: number;
  roles: AnyRecord[];
  onDone: (result: AnyRecord, message: string) => void;
}) {
  const [form, setForm] = useState({ fullName: "", email: "", roleName: "Company Admin" });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const valid = form.fullName.trim() !== "" && isEmail(form.email.trim());

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.createTenantUser(tenantId, {
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        roleName: form.roleName,
      });
      onDone(res, `${form.email.trim()} created — copy the temporary password now`);
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not create the user"); }
    finally { setBusy(false); }
  };

  return (
    <div className="mb-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
      <p className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-500">New user</p>
      {err && <p className="mb-2 text-xs font-medium text-red-600">{err}</p>}
      <div className="space-y-3">
        <PField label="Full name">
          <PInput value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} placeholder="Jane Doe" />
        </PField>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Email (sign-in address)">
            <PInput type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} placeholder="jane@acme.com" />
          </PField>
          <PField label="Role">
            <PSelect value={form.roleName} onChange={(e) => setForm({ ...form, roleName: e.target.value })}>
              {roles.map((r) => <option key={String(r.name)} value={String(r.name)}>{String(r.name)}</option>)}
            </PSelect>
          </PField>
        </div>
        <p className="text-[11px] text-slate-500">
          The account is created active with a one-time password shown once here — so a locked-out customer can be
          recovered on the call, without waiting on SMTP.
        </p>
        <PButton disabled={busy || !valid} onClick={submit}>{busy ? "Creating…" : "Create user"}</PButton>
      </div>
    </div>
  );
}

function EditTenantUserForm({ tenantId, user, roles, onDone, onInviteSent }: {
  tenantId: number;
  user: AnyRecord;
  roles: AnyRecord[];
  onDone: (message: string) => void;
  onInviteSent: (message: string) => void;
}) {
  const currentEmail = String(user.email ?? "");
  const [form, setForm] = useState({
    fullName: String(user.fullName ?? ""),
    email: currentEmail,
    roleName: String(user.roleName ?? ""),
    status: String(user.status ?? "Active"),
  });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [issuedInvite, setIssuedInvite] = useState<{
    email: string; url?: string; token?: string; emailSent: boolean;
  } | null>(null);

  const emailChanged = form.email.trim().toLowerCase() !== currentEmail.toLowerCase();
  const valid = form.fullName.trim() !== "" && isEmail(form.email.trim());

  const save = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.updateTenantUser(tenantId, Number(user.id), {
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        roleName: form.roleName,
        status: form.status,
      });
      const revoked = Number(res?.sessionsRevoked ?? 0);
      onDone(revoked > 0
        ? `User updated — ${revoked} active session${revoked === 1 ? "" : "s"} revoked`
        : "User updated");
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not update the user"); }
    finally { setBusy(false); }
  };

  const resendInvite = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.resendTenantUserInvite(tenantId, Number(user.id));
      setIssuedInvite({
        email: String(res.email ?? currentEmail),
        url: res.activationUrl ? String(res.activationUrl) : undefined,
        token: res.activationToken ? String(res.activationToken) : undefined,
        emailSent: res.emailSent === true,
      });
      onInviteSent(res?.emailSent
        ? `Set-password invite emailed to ${String(res.email)}`
        : "Invite re-armed — deliver the one-time activation link shown below");
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not re-send the invite"); }
    finally { setBusy(false); }
  };

  return (
    <div className="mt-3 space-y-3 border-t border-slate-200 pt-3">
      {err && <p className="text-xs font-medium text-red-600">{err}</p>}
      <PField label="Full name">
        <PInput value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
      </PField>
      <PField label="Email (sign-in address)">
        <PInput type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
      </PField>
      {emailChanged && (
        <p className="rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1.5 text-[11px] text-amber-800">
          This is the address they sign in with. Saving signs them out everywhere; their password is unchanged.
        </p>
      )}
      <div className="grid grid-cols-2 gap-3">
        <PField label="Role">
          <PSelect value={form.roleName} onChange={(e) => setForm({ ...form, roleName: e.target.value })}>
            {!roles.some((r) => String(r.name) === form.roleName) && <option value={form.roleName}>{form.roleName}</option>}
            {roles.map((r) => <option key={String(r.name)} value={String(r.name)}>{String(r.name)}</option>)}
          </PSelect>
        </PField>
        <PField label="Status">
          <PSelect value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })}>
            <option value="Active">Active</option>
            <option value="Disabled">Disabled</option>
            <option value="Pending">Pending (awaiting set-password)</option>
          </PSelect>
        </PField>
      </div>
      <div className="flex flex-wrap gap-2">
        <PButton disabled={busy || !valid} onClick={save}>{busy ? "Saving…" : "Save user"}</PButton>
        <PButton variant="ghost" disabled={busy} onClick={resendInvite}>Re-send set-password invite</PButton>
      </div>
      {issuedInvite && (
        <ActivationLinkPanel {...issuedInvite} onDismiss={() => setIssuedInvite(null)} />
      )}
    </div>
  );
}

// ── Per-feature commercial terms ─────────────────────────────────────────────
// This is the flexible half of billing: any feature can be free for one customer,
// a flat fee for another, and billed per event for a third. A feature with no term
// here simply follows its package default.

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

function TenantBillingPlanSection({ tenantId, canManage, onNotice }: {
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
