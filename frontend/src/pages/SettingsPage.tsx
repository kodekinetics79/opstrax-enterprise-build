import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, Building2, Check, Code2, Copy, ExternalLink, Globe, Key, Languages, Lock, Mail, Phone, RefreshCw, Settings, Shield, Trash2, Webhook } from "lucide-react";
import { useLocalizationSettings, useUpdateLocaleSettings, useUpdateUserPreferences } from "@/hooks/useBatch6";
import { useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useI18n, LOCALES } from "@/i18n";
import type { LocaleCode } from "@/i18n";
import type { AnyRecord } from "@/types";
import { settingsApi } from "@/services/settingsApi";

const DATE_FORMATS = ["MM/DD/YYYY", "DD/MM/YYYY", "YYYY-MM-DD"];
const TIMEZONES = [
  "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
  "America/Toronto", "Europe/London", "Asia/Riyadh", "Asia/Dubai", "Asia/Karachi",
];
const CURRENCIES = ["USD", "CAD", "SAR", "AED", "PKR", "EUR", "GBP"];
const DISTANCE_UNITS = ["Miles", "Kilometers"];
const VOLUME_UNITS = ["Gallons", "Liters"];

type Tab = "general" | "notifications" | "security" | "api";

const NOTIF_CATEGORIES = [
  { key: "speed_alert",      label: "Speed Alert",           description: "Driver exceeds speed threshold" },
  { key: "geofence_breach",  label: "Geofence Breach",       description: "Vehicle enters or exits a zone" },
  { key: "idle_alert",       label: "Excessive Idling",      description: "Vehicle idles beyond configured limit" },
  { key: "sos_panic",        label: "SOS / Panic Button",    description: "Driver triggers emergency SOS" },
  { key: "hos_violation",    label: "HOS Violation",         description: "Driver approaches or exceeds HOS limits" },
  { key: "maintenance_due",  label: "Maintenance Due",       description: "Scheduled PM is approaching or overdue" },
  { key: "accident_event",   label: "Accident / Collision",  description: "Dashcam detects a collision event" },
  { key: "sla_breach",       label: "SLA Breach Risk",       description: "Shipment at risk of missing SLA window" },
  { key: "fuel_anomaly",     label: "Fuel Anomaly",          description: "Unusual fuel consumption detected" },
  { key: "device_offline",   label: "Device Offline",        description: "Telematics device goes offline" },
];

const CHANNELS = ["Email", "SMS", "In-App"] as const;
type Channel = typeof CHANNELS[number];

function buildDefaultNotifPrefs() {
  const prefs: Record<string, Record<Channel, boolean>> = {};
  for (const { key } of NOTIF_CATEGORIES) {
    prefs[key] = { Email: key === "sos_panic" || key === "accident_event", SMS: key === "sos_panic", "In-App": true };
  }
  return prefs;
}

function ToggleSwitch({ checked, onChange, disabled, label }: { checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; label?: string }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked ? "true" : "false"}
      aria-label={label ?? (checked ? "Enabled" : "Disabled")}
      title={label ?? (checked ? "Enabled" : "Disabled")}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-teal-500 disabled:opacity-40 ${checked ? "bg-teal-500" : "bg-slate-200"}`}
    >
      <span className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow ring-0 transition-transform ${checked ? "translate-x-4" : "translate-x-0"}`} />
    </button>
  );
}

function SectionHeader({ icon: Icon, title, description }: { icon: React.ElementType; title: string; description: string }) {
  return (
    <div className="flex items-start gap-3 border-b border-slate-200 pb-4 mb-5">
      <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-slate-100">
        <Icon className="h-4 w-4 text-slate-500" />
      </div>
      <div>
        <h2 className="text-sm font-semibold text-slate-900">{title}</h2>
        <p className="text-xs text-slate-500 mt-0.5">{description}</p>
      </div>
    </div>
  );
}

function SaveRow({ onSave, isPending, saved, canSave }: { onSave: () => void; isPending: boolean; saved: boolean; canSave: boolean }) {
  return (
    <div className="flex items-center gap-3 pt-4 border-t border-slate-100">
      <button
        type="button"
        className="rounded-xl bg-teal-500 hover:bg-teal-400 text-slate-950 font-bold text-sm px-5 py-2 transition disabled:opacity-50"
        onClick={onSave}
        disabled={isPending || !canSave}
        title={!canSave ? "You do not have permission to perform this action." : undefined}
      >
        {isPending ? "Saving…" : "Save Changes"}
      </button>
      {saved && (
        <span className="flex items-center gap-1.5 text-xs text-emerald-600 font-semibold">
          <Check className="h-3.5 w-3.5" /> Saved
        </span>
      )}
    </div>
  );
}

export function SettingsPage() {
  const { t, locale, setLocale, isRtl } = useI18n();
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const canUpdateSettings = hasPermission(PERMISSIONS.SETTINGS_UPDATE);

  const queryClient = useQueryClient();

  const settingsQ         = useLocalizationSettings();
  const updateSettingsMut = useUpdateLocaleSettings();
  const updatePrefsMut    = useUpdateUserPreferences();

  const serverSettings = ((settingsQ.data as AnyRecord[] | undefined)?.[0]) as AnyRecord | undefined;

  const [tab, setTab] = useState<Tab>("general");

  const [localeForm, setLocaleForm] = useState({
    defaultLanguage: "en-US",
    defaultCountry:  "US",
    timezone:        "America/New_York",
    dateFormat:      "MM/DD/YYYY",
    currency:        "USD",
    distanceUnit:    "Miles",
    volumeUnit:      "Gallons",
  });
  const [localeSaved, setLocaleSaved] = useState(false);

  // ── Company profile — real, persisted per tenant ──────────────────────────
  const companyProfileQ = useQuery({ queryKey: ["settings-company-profile"], queryFn: settingsApi.companyProfileGet });
  const [companyForm, setCompanyForm] = useState({
    displayName: "", addressLine1: "", city: "", state: "", country: "US",
    phone: "", contactEmail: "", website: "",
  });
  useEffect(() => {
    if (!companyProfileQ.data) return;
    const p = companyProfileQ.data as AnyRecord;
    setCompanyForm({
      displayName:  String(p.displayName ?? ""),
      addressLine1: String(p.addressLine1 ?? ""),
      city:         String(p.city ?? ""),
      state:        String(p.state ?? ""),
      country:      String(p.country ?? "US"),
      phone:        String(p.phone ?? ""),
      contactEmail: String(p.contactEmail ?? ""),
      website:      String(p.website ?? ""),
    });
  }, [companyProfileQ.data]);
  const companyProfileMut = useMutation({
    mutationFn: () => settingsApi.companyProfilePut(companyForm),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["settings-company-profile"] }),
  });
  const [companySaved, setCompanySaved] = useState(false);
  function saveCompany() {
    companyProfileMut.mutate(undefined, {
      onSuccess: () => { setCompanySaved(true); setTimeout(() => setCompanySaved(false), 2500); },
    });
  }

  // ── Notification preferences — real, per-user ─────────────────────────────
  const notifPrefsQ = useQuery({ queryKey: ["settings-notification-prefs"], queryFn: settingsApi.notificationPrefsGet });
  const [notifPrefs, setNotifPrefs] = useState(buildDefaultNotifPrefs());
  useEffect(() => {
    const prefs = notifPrefsQ.data?.prefs as Record<string, Record<Channel, boolean>> | undefined;
    if (prefs && Object.keys(prefs).length > 0) setNotifPrefs(prefs);
  }, [notifPrefsQ.data]);
  const notifPrefsMut = useMutation({
    mutationFn: () => settingsApi.notificationPrefsPut(notifPrefs),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["settings-notification-prefs"] }),
  });
  const [notifSaved, setNotifSaved] = useState(false);
  function saveNotif() {
    notifPrefsMut.mutate(undefined, {
      onSuccess: () => { setNotifSaved(true); setTimeout(() => setNotifSaved(false), 2500); },
    });
  }

  // ── Security policy — real, tenant-scoped (/api/security/settings) ───────
  const securitySettingsQ = useQuery({ queryKey: ["settings-security"], queryFn: settingsApi.securitySettingsGet });
  const [securityForm, setSecurityForm] = useState({
    sessionTimeoutMin: "60",
    requireMfa:        false,
    auditRetentionDays:"90",
  });
  useEffect(() => {
    if (!securitySettingsQ.data) return;
    const s = securitySettingsQ.data as AnyRecord;
    setSecurityForm({
      sessionTimeoutMin:  String(s.sessionIdleTimeoutMinutes ?? "60"),
      requireMfa:         Boolean(s.mfaRequired),
      auditRetentionDays: String(s.auditRetentionDays ?? "90"),
    });
  }, [securitySettingsQ.data]);
  const securitySettingsMut = useMutation({
    mutationFn: () => settingsApi.securitySettingsPut({
      sessionIdleTimeoutMinutes: Number(securityForm.sessionTimeoutMin) || 60,
      mfaRequired: securityForm.requireMfa,
      auditRetentionDays: Number(securityForm.auditRetentionDays) || 90,
    }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["settings-security"] }),
  });
  const [securitySaved, setSecuritySaved] = useState(false);
  function saveSecurity() {
    securitySettingsMut.mutate(undefined, {
      onSuccess: () => { setSecuritySaved(true); setTimeout(() => setSecuritySaved(false), 2500); },
    });
  }

  // ── API keys — real, hashed server-side; raw value shown once on creation ─
  const apiKeysQ = useQuery({ queryKey: ["settings-api-keys"], queryFn: settingsApi.apiKeysList });
  const [justCreatedKey, setJustCreatedKey] = useState<{ apiKey: string; keyPrefix: string } | null>(null);
  const [apiKeyCopied, setApiKeyCopied] = useState(false);
  const createApiKeyMut = useMutation({
    mutationFn: () => settingsApi.apiKeyCreate(),
    onSuccess: (data) => {
      setJustCreatedKey({ apiKey: String(data.apiKey), keyPrefix: String(data.keyPrefix) });
      void queryClient.invalidateQueries({ queryKey: ["settings-api-keys"] });
    },
  });
  const revokeApiKeyMut = useMutation({
    mutationFn: (id: number) => settingsApi.apiKeyRevoke(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["settings-api-keys"] }),
  });
  function copyApiKey(value: string) {
    void navigator.clipboard.writeText(value);
    setApiKeyCopied(true);
    setTimeout(() => setApiKeyCopied(false), 2000);
  }

  // ── Webhook — real, persisted per tenant ──────────────────────────────────
  const webhookQ = useQuery({ queryKey: ["settings-webhook"], queryFn: settingsApi.webhookGet });
  const [webhookUrl, setWebhookUrl] = useState("");
  const [webhookEvents, setWebhookEvents] = useState<string[]>([]);
  useEffect(() => {
    if (!webhookQ.data) return;
    const w = webhookQ.data as AnyRecord;
    setWebhookUrl(String(w.endpointUrl ?? ""));
    setWebhookEvents(Array.isArray(w.events) ? (w.events as string[]) : []);
  }, [webhookQ.data]);
  const webhookMut = useMutation({
    mutationFn: () => settingsApi.webhookPut({ endpointUrl: webhookUrl || null, events: webhookEvents }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["settings-webhook"] }),
  });
  const rotateWebhookSecretMut = useMutation({ mutationFn: settingsApi.webhookRotateSecret });
  const [apiSaved, setApiSaved] = useState(false);
  function saveApi() {
    webhookMut.mutate(undefined, {
      onSuccess: () => { setApiSaved(true); setTimeout(() => setApiSaved(false), 2500); },
    });
  }

  function saveLocale() {
    updateSettingsMut.mutate({ ...localeForm });
    updatePrefsMut.mutate({ language: localeForm.defaultLanguage, countryCode: localeForm.defaultCountry });
    setLocale(localeForm.defaultLanguage as LocaleCode);
    setLocaleSaved(true);
    setTimeout(() => setLocaleSaved(false), 2500);
  }

  function toggleWebhookEvent(evt: string) {
    setWebhookEvents((prev) => prev.includes(evt) ? prev.filter((e) => e !== evt) : [...prev, evt]);
  }

  const TABS: { key: Tab; label: string; icon: React.ElementType }[] = [
    { key: "general",       label: "General",       icon: Globe },
    { key: "notifications", label: "Notifications", icon: Bell },
    { key: "security",      label: "Security",      icon: Shield },
    { key: "api",           label: "API & Webhooks",icon: Webhook },
  ];

  function SelectField({ label, value, onChange, options }: { label: string; value: string; onChange: (v: string) => void; options: string[] }) {
    return (
      <div className="grid grid-cols-[1fr_1.5fr] items-center gap-4 border-b border-slate-100 pb-3">
        <label className="text-sm text-slate-600">{label}</label>
        <select aria-label={label} className="field w-full" value={value} onChange={(e) => onChange(e.target.value)} disabled={!canUpdateSettings}>
          {options.map((o) => <option key={o} value={o}>{o}</option>)}
        </select>
      </div>
    );
  }

  function TextField({ label, value, onChange, type = "text", placeholder }: { label: string; value: string; onChange: (v: string) => void; type?: string; placeholder?: string }) {
    return (
      <div className="grid grid-cols-[1fr_1.5fr] items-center gap-4 border-b border-slate-100 pb-3">
        <label className="text-sm text-slate-600">{label}</label>
        <input aria-label={label} type={type} className="field w-full" value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} disabled={!canUpdateSettings} />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6 max-w-3xl">
      <div>
        <h1 className="text-xl font-bold text-slate-900 flex items-center gap-2">
          <Settings className="h-5 w-5 text-slate-400" />{t("settings")}
        </h1>
        <p className="text-sm text-slate-500 mt-0.5">Platform preferences, notifications, security and API access</p>
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 flex-wrap">
        {TABS.map(({ key, label, icon: Icon }) => (
          <button
            type="button"
            key={key}
            onClick={() => setTab(key)}
            className={`flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition ${tab === key ? "bg-white text-slate-900 shadow-sm" : "text-slate-500 hover:text-slate-700"}`}
          >
            <Icon className="h-3.5 w-3.5" />{label}
          </button>
        ))}
      </div>

      {/* ── GENERAL ──────────────────────────────────────────────────────────── */}
      {tab === "general" && (
        <div className="flex flex-col gap-5">
          {/* Company Profile */}
          <div className="panel space-y-4">
            <SectionHeader icon={Building2} title="Company Profile" description="Display name, contact info and business address" />
            <div className="space-y-3">
              <TextField label="Company Name"    value={companyForm.displayName}  onChange={(v) => setCompanyForm((f) => ({ ...f, displayName: v }))} />
              <TextField label="Address"         value={companyForm.addressLine1} onChange={(v) => setCompanyForm((f) => ({ ...f, addressLine1: v }))} />
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="text-xs text-slate-500">City</label>
                  <input aria-label="City" className="field mt-1 w-full" value={companyForm.city} onChange={(e) => setCompanyForm((f) => ({ ...f, city: e.target.value }))} disabled={!canUpdateSettings} />
                </div>
                <div>
                  <label className="text-xs text-slate-500">State / Region</label>
                  <input aria-label="State / Region" className="field mt-1 w-full" value={companyForm.state} onChange={(e) => setCompanyForm((f) => ({ ...f, state: e.target.value }))} disabled={!canUpdateSettings} />
                </div>
                <div>
                  <label className="text-xs text-slate-500">Country</label>
                  <select aria-label="Country" className="field mt-1 w-full" value={companyForm.country} onChange={(e) => setCompanyForm((f) => ({ ...f, country: e.target.value }))} disabled={!canUpdateSettings}>
                    {["US","CA","SA","AE","PK","GB"].map((c) => <option key={c}>{c}</option>)}
                  </select>
                </div>
              </div>
              <TextField label="Phone"    value={companyForm.phone}        onChange={(v) => setCompanyForm((f) => ({ ...f, phone: v }))}        type="tel" />
              <TextField label="Email"    value={companyForm.contactEmail} onChange={(v) => setCompanyForm((f) => ({ ...f, contactEmail: v }))} type="email" />
              <TextField label="Website"  value={companyForm.website}      onChange={(v) => setCompanyForm((f) => ({ ...f, website: v }))} />
            </div>
            <SaveRow onSave={saveCompany} isPending={companyProfileMut.isPending} saved={companySaved} canSave={canUpdateSettings} />
          </div>

          {/* Language */}
          <div className="panel space-y-4">
            <SectionHeader icon={Languages} title={t("language")} description="Interface language and text direction" />
            <p className="text-xs text-slate-500 -mt-2">{t("language_rtl_note")}</p>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {(Object.entries(LOCALES) as [LocaleCode, typeof LOCALES[LocaleCode]][]).map(([code, meta]) => (
                <button
                  type="button"
                  key={code}
                  onClick={() => { setLocale(code); setLocaleForm((f) => ({ ...f, defaultLanguage: code })); }}
                  className={`relative flex flex-col items-start rounded-xl border p-3 text-start transition-all ${locale === code ? "border-violet-400/40 bg-violet-50" : "border-slate-200 bg-white hover:border-slate-300"}`}
                >
                  {locale === code && (
                    <span className="absolute top-2 inset-e-2 h-4 w-4 rounded-full bg-violet-400 flex items-center justify-center">
                      <Check className="h-2.5 w-2.5 text-slate-950" />
                    </span>
                  )}
                  <p className="text-xs font-semibold text-slate-900">{meta.nativeLabel}</p>
                  <p className="text-[10px] text-slate-500 mt-0.5">{meta.label}</p>
                  {meta.rtl && <span className="mt-1 rounded border border-amber-400/20 bg-amber-400/10 px-1 py-0.5 text-[9px] font-bold text-amber-400">RTL</span>}
                </button>
              ))}
            </div>
            {isRtl && <div className="rounded-xl border border-emerald-400/20 bg-emerald-50 p-3 text-xs text-emerald-700">RTL layout is active.</div>}
          </div>

          {/* Localization */}
          <div className="panel space-y-4">
            <SectionHeader icon={Globe} title={t("tenant_settings")} description="Timezone, date format, units and currency" />
            <div className="space-y-3">
              <SelectField label={t("default_country")} value={localeForm.defaultCountry} onChange={(v) => setLocaleForm((f) => ({ ...f, defaultCountry: v }))} options={["US","CA","SA","AE","PK"]} />
              <SelectField label={t("timezone")}        value={localeForm.timezone}        onChange={(v) => setLocaleForm((f) => ({ ...f, timezone: v }))}        options={TIMEZONES} />
              <SelectField label={t("date_format")}     value={localeForm.dateFormat}      onChange={(v) => setLocaleForm((f) => ({ ...f, dateFormat: v }))}      options={DATE_FORMATS} />
              <SelectField label={t("currency")}        value={localeForm.currency}        onChange={(v) => setLocaleForm((f) => ({ ...f, currency: v }))}        options={CURRENCIES} />
              <SelectField label={t("distance_unit")}   value={localeForm.distanceUnit}    onChange={(v) => setLocaleForm((f) => ({ ...f, distanceUnit: v }))}    options={DISTANCE_UNITS} />
              <SelectField label={t("volume_unit")}     value={localeForm.volumeUnit}      onChange={(v) => setLocaleForm((f) => ({ ...f, volumeUnit: v }))}      options={VOLUME_UNITS} />
            </div>
            <SaveRow onSave={saveLocale} isPending={updateSettingsMut.isPending} saved={localeSaved} canSave={canUpdateSettings} />
          </div>

          {/* Platform info */}
          <div className="panel space-y-4">
            <SectionHeader icon={Code2} title="Platform & Developer Info" description="Build version, stack and support contacts" />
            <div className="grid grid-cols-2 gap-x-8 gap-y-3 text-sm sm:grid-cols-3">
              {[
                ["Product",     "OpsTrax Transport Management Solution"],
                ["Developer",   "Kode Kinetics"],
                ["Version",     "Enterprise Build"],
                ["Environment", "Local / Seeded"],
                ["Frontend",    "React 19 · TypeScript · Vite"],
                ["Backend",     "ASP.NET Core 8 · MySQL 8.4"],
              ].map(([k, v]) => (
                <div key={k}>
                  <p className="text-[10px] text-slate-500 uppercase tracking-widest">{k}</p>
                  <p className="font-semibold text-slate-700 mt-0.5 text-xs">{v}</p>
                </div>
              ))}
            </div>
            <div className="border-t border-slate-200 pt-3 space-y-1.5">
              <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Support</p>
              <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer" className="flex items-center gap-2 text-xs text-teal-600 hover:text-teal-500 transition">
                <Globe className="h-3.5 w-3.5" />www.kodekinetics.com<ExternalLink className="h-3 w-3 opacity-60" />
              </a>
              <a href="mailto:info@kodekinetics.com" className="flex items-center gap-2 text-xs text-slate-500 hover:text-slate-700 transition">
                <Mail className="h-3.5 w-3.5" />info@kodekinetics.com
              </a>
              <a href="tel:+15714305333" className="flex items-center gap-2 text-xs text-slate-500 hover:text-slate-700 transition">
                <Phone className="h-3.5 w-3.5" />+1 571 430 5333
              </a>
            </div>
          </div>

          {serverSettings && (
            <div className="panel space-y-2">
              <p className="section-title text-slate-500">Server Locale State</p>
              <div className="grid grid-cols-2 gap-x-6 gap-y-1 text-xs">
                {(Object.entries(serverSettings) as [string, unknown][])
                  .filter(([k]) => !["id","tenant_id","created_at","updated_at"].includes(k))
                  .map(([k, v]) => (
                    <div key={k} className="flex justify-between border-b border-slate-100 py-1">
                      <span className="text-slate-500">{k.replace(/_/g, " ")}</span>
                      <span className="text-slate-700 font-mono">{String(v ?? "—")}</span>
                    </div>
                  ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* ── NOTIFICATIONS ────────────────────────────────────────────────────── */}
      {tab === "notifications" && (
        <div className="panel space-y-5">
          <SectionHeader icon={Bell} title="Notification Preferences" description="Choose how and where you receive platform alerts" />
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  <th className="text-left text-xs font-semibold text-slate-500 uppercase tracking-wide pb-2 w-1/2">Event</th>
                  {CHANNELS.map((ch) => (
                    <th key={ch} className="text-center text-xs font-semibold text-slate-500 uppercase tracking-wide pb-2 px-4">{ch}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {NOTIF_CATEGORIES.map(({ key, label, description }) => (
                  <tr key={key} className="hover:bg-slate-50 transition">
                    <td className="py-3 pr-4">
                      <p className="font-medium text-slate-800">{label}</p>
                      <p className="text-xs text-slate-400 mt-0.5">{description}</p>
                    </td>
                    {CHANNELS.map((ch) => (
                      <td key={ch} className="py-3 px-4 text-center">
                        <div className="flex justify-center">
                          <ToggleSwitch
                            checked={notifPrefs[key]?.[ch] ?? false}
                            onChange={(v) => setNotifPrefs((prev) => ({ ...prev, [key]: { ...prev[key], [ch]: v } }))}
                            disabled={!canUpdateSettings}
                          />
                        </div>
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <SaveRow onSave={saveNotif} isPending={notifPrefsMut.isPending} saved={notifSaved} canSave={canUpdateSettings} />
        </div>
      )}

      {/* ── SECURITY ─────────────────────────────────────────────────────────── */}
      {tab === "security" && (
        <div className="flex flex-col gap-5">
          <div className="panel space-y-4">
            <SectionHeader icon={Lock} title="Session & Access" description="Idle timeout and audit log retention policy" />
            <div className="space-y-3">
              <SelectField label="Session Timeout"    value={securityForm.sessionTimeoutMin} onChange={(v) => setSecurityForm((f) => ({ ...f, sessionTimeoutMin: v }))} options={["15","30","60","120","240"]} />
              <TextField  label="Audit Retention (days)" value={securityForm.auditRetentionDays} onChange={(v) => setSecurityForm((f) => ({ ...f, auditRetentionDays: v }))} type="number" />
            </div>
            <SaveRow onSave={saveSecurity} isPending={securitySettingsMut.isPending} saved={securitySaved} canSave={canUpdateSettings} />
          </div>

          <div className="panel space-y-4">
            <SectionHeader icon={Shield} title="Two-Factor Authentication" description="Require MFA for all platform users" />
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-slate-800">Require 2FA for all users</p>
                <p className="text-xs text-slate-500 mt-0.5">When enabled, every user must enrol a TOTP or email OTP before accessing the platform.</p>
              </div>
              <ToggleSwitch checked={securityForm.requireMfa} onChange={(v) => setSecurityForm((f) => ({ ...f, requireMfa: v }))} disabled={!canUpdateSettings} label="Require 2FA for all users" />
            </div>
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-700">
              2FA enforcement is controlled by tenant policy and sign-in configuration. If your organization has not enabled it yet, the login flow will continue to respect the current auth policy.
            </div>
          </div>

          <div className="panel space-y-4">
            <SectionHeader icon={Lock} title="Active Session" description="Details about your current authenticated session" />
            <div className="grid grid-cols-2 gap-3 text-xs">
              {[
                ["User",     String(session?.user?.fullName ?? session?.user?.email ?? "—")],
                ["Role",     String(session?.role ?? "—")],
                ["Tenant",   String(session?.company?.name ?? session?.company?.id ?? "—")],
                ["Session",  "Active · Server-verified session token"],
              ].map(([k, v]) => (
                <div key={k} className="rounded-lg bg-slate-50 border border-slate-200 p-3">
                  <p className="text-slate-500 font-medium">{k}</p>
                  <p className="text-slate-900 font-semibold mt-0.5">{v}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* ── API & WEBHOOKS ───────────────────────────────────────────────────── */}
      {tab === "api" && (
        <div className="flex flex-col gap-5">
          <div className="panel space-y-4">
            <SectionHeader icon={Key} title="API Keys" description="Server-to-server credentials, hashed at rest — the raw key is shown once" />

            {justCreatedKey && (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-2">
                <p className="text-xs font-semibold text-amber-800">New key created — copy it now, it will not be shown again.</p>
                <div className="flex items-center gap-2">
                  <code className="flex-1 rounded-lg border border-amber-200 bg-white px-3 py-2 text-xs font-mono text-slate-700 overflow-x-auto select-all">
                    {justCreatedKey.apiKey}
                  </code>
                  <button type="button" className="btn-secondary text-xs shrink-0" onClick={() => copyApiKey(justCreatedKey.apiKey)}>
                    {apiKeyCopied ? <><Check className="h-3.5 w-3.5" /> Copied</> : <><Copy className="h-3.5 w-3.5" /> Copy</>}
                  </button>
                </div>
                <div>
                  <p className="text-xs text-slate-500 font-semibold mb-1.5">Authentication Header</p>
                  <code className="block rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs font-mono text-slate-600">
                    Authorization: Bearer {justCreatedKey.apiKey}
                  </code>
                </div>
                <button type="button" className="text-xs font-semibold text-amber-800 underline" onClick={() => setJustCreatedKey(null)}>
                  I've saved this key
                </button>
              </div>
            )}

            {apiKeysQ.isLoading ? (
              <p className="text-xs text-slate-400">Loading keys…</p>
            ) : (apiKeysQ.data ?? []).length === 0 ? (
              <p className="text-xs text-slate-500">No API keys yet. Generate one to authenticate server-to-server calls.</p>
            ) : (
              <div className="divide-y divide-slate-100 border border-slate-200 rounded-lg overflow-hidden">
                {(apiKeysQ.data as AnyRecord[]).map((k) => (
                  <div key={String(k.id)} className="flex items-center justify-between px-3 py-2.5 text-xs">
                    <div>
                      <p className="font-mono text-slate-700">{String(k.keyPrefix)}_••••{String(k.lastFour)}</p>
                      <p className="text-slate-400 mt-0.5">
                        {k.label ? `${k.label} · ` : ""}
                        Created {k.createdAt ? new Date(String(k.createdAt)).toLocaleDateString() : "—"}
                        {k.revokedAt ? " · Revoked" : ""}
                      </p>
                    </div>
                    {!k.revokedAt && canUpdateSettings && (
                      <button
                        type="button"
                        className="flex items-center gap-1 text-rose-600 hover:text-rose-700 font-semibold"
                        onClick={() => revokeApiKeyMut.mutate(Number(k.id))}
                        disabled={revokeApiKeyMut.isPending}
                      >
                        <Trash2 className="h-3.5 w-3.5" /> Revoke
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}

            {canUpdateSettings && (
              <button
                type="button"
                className="rounded-xl bg-teal-500 hover:bg-teal-400 text-slate-950 font-bold text-sm px-4 py-2 transition disabled:opacity-50"
                onClick={() => createApiKeyMut.mutate()}
                disabled={createApiKeyMut.isPending}
              >
                {createApiKeyMut.isPending ? "Generating…" : "Generate New Key"}
              </button>
            )}

            <div className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
              Keep keys secret. Treat them like a password — do not expose them in client-side code or public repositories. Revoke immediately if compromised.
            </div>
          </div>

          <div className="panel space-y-4">
            <SectionHeader icon={Webhook} title="Webhooks" description="Configure the endpoint and events your integration subscribes to" />
            <div className="space-y-3">
              <div>
                <label className="text-sm text-slate-600">Endpoint URL</label>
                <input
                  type="url"
                  aria-label="Webhook endpoint URL"
                  className="field mt-1 w-full"
                  value={webhookUrl}
                  onChange={(e) => setWebhookUrl(e.target.value)}
                  placeholder="https://your-server.com/webhooks/opstrax"
                  disabled={!canUpdateSettings}
                />
              </div>
              <div>
                <p className="text-sm text-slate-600 mb-2">Events to deliver</p>
                <div className="flex flex-wrap gap-2">
                  {["shipment.created","shipment.delivered","alert.triggered","hos.violation","geofence.breach","maintenance.due","accident.detected","driver.sos"].map((evt) => (
                    <button
                      type="button"
                      key={evt}
                      onClick={() => canUpdateSettings && toggleWebhookEvent(evt)}
                      className={`rounded-full border px-2.5 py-1 text-xs font-medium transition ${webhookEvents.includes(evt) ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 bg-white text-slate-500 hover:text-slate-700"}`}
                    >
                      {evt}
                    </button>
                  ))}
                </div>
              </div>
            </div>

            <div className="rounded-lg bg-slate-50 border border-slate-200 p-3 flex items-center justify-between gap-3">
              <div className="text-xs text-slate-600">
                Signing secret: {webhookQ.data?.secretConfigured ? <span className="font-semibold text-emerald-700">configured</span> : <span className="text-slate-400">not yet generated</span>}
                <p className="text-slate-400 mt-0.5">Payloads are signed with HMAC-SHA256 in the <code className="font-mono">X-OpsTrax-Signature</code> header.</p>
              </div>
              <button
                type="button"
                className="btn-secondary text-xs shrink-0 flex items-center gap-1"
                onClick={() => rotateWebhookSecretMut.mutate()}
                disabled={!canUpdateSettings || rotateWebhookSecretMut.isPending}
              >
                <RefreshCw className="h-3.5 w-3.5" /> Rotate Secret
              </button>
            </div>
            {rotateWebhookSecretMut.data && (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-1.5">
                <p className="text-xs font-semibold text-amber-800">New signing secret — copy it now, it will not be shown again.</p>
                <code className="block rounded-lg border border-amber-200 bg-white px-3 py-2 text-xs font-mono text-slate-700 overflow-x-auto select-all">
                  {String(rotateWebhookSecretMut.data.signingSecret)}
                </code>
              </div>
            )}

            <SaveRow onSave={saveApi} isPending={webhookMut.isPending} saved={apiSaved} canSave={canUpdateSettings} />
          </div>
        </div>
      )}
    </div>
  );
}
