import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, Building2, Check, Copy, ExternalLink, Globe, Info, Key, Languages, Lock, Mail, Phone, RefreshCw, Settings, Shield, Trash2, Webhook } from "lucide-react";
import { useLocalizationSettings, useUpdateLocaleSettings, useUpdateUserPreferences } from "@/hooks/useBatch6";
import { useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { useI18n, LOCALES } from "@/i18n";
import type { LocaleCode } from "@/i18n";
import type { AnyRecord } from "@/types";
import { settingsApi } from "@/services/settingsApi";
import { aboutApi } from "@/services/aboutApi";
import { ChangePasswordCard } from "@/components/ChangePasswordCard";
import { SsoConnectionsPanel } from "@/components/SsoConnectionsPanel";

const DATE_FORMATS = ["MM/DD/YYYY", "DD/MM/YYYY", "YYYY-MM-DD"];
const TIMEZONES = [
  "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
  "America/Toronto", "Europe/London", "Asia/Riyadh", "Asia/Dubai", "Asia/Karachi",
];
const CURRENCIES = ["USD", "CAD", "SAR", "AED", "PKR", "EUR", "GBP"];
const DISTANCE_UNITS = ["Miles", "Kilometers"];
const VOLUME_UNITS = ["Gallons", "Liters"];

type Section = "organization" | "preferences" | "notifications" | "security" | "api" | "about";

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

/** Neumorphic toggle — inset track, extruded knob (shared IAM language). */
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
      className="iam-toggle"
    >
      <span className="iam-toggle-knob" />
    </button>
  );
}

function SectionHeader({ icon: Icon, title, description }: { icon: React.ElementType; title: string; description: string }) {
  return (
    <div className="flex items-start gap-3 border-b border-slate-200/70 pb-4 mb-5 min-w-0">
      <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-white shadow-[inset_2px_2px_5px_rgba(141,157,184,.22),inset_-3px_-3px_7px_rgba(255,255,255,.95)] border border-slate-200/70">
        <Icon className="h-4 w-4 text-teal-600" />
      </div>
      <div className="min-w-0">
        <h2 className="text-sm font-bold text-slate-900 truncate">{title}</h2>
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
        className="rounded-xl bg-teal-600 hover:bg-teal-500 text-white font-bold text-sm px-5 py-2 shadow-[0_3px_10px_rgba(13,148,136,.28),inset_0_1px_0_rgba(255,255,255,.25)] transition active:translate-y-px disabled:opacity-50"
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

  const [section, setSection] = useState<Section>("organization");

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

  // Hydrate the localization form from the tenant's persisted server state so
  // the controls reflect reality instead of hardcoded defaults.
  useEffect(() => {
    if (!serverSettings) return;
    setLocaleForm((f) => ({
      defaultLanguage: String(serverSettings.default_language ?? serverSettings.defaultLanguage ?? f.defaultLanguage),
      defaultCountry:  String(serverSettings.default_country ?? serverSettings.defaultCountry ?? f.defaultCountry),
      timezone:        String(serverSettings.timezone ?? f.timezone),
      dateFormat:      String(serverSettings.date_format ?? serverSettings.dateFormat ?? f.dateFormat),
      currency:        String(serverSettings.currency ?? f.currency),
      distanceUnit:    String(serverSettings.distance_unit ?? serverSettings.distanceUnit ?? f.distanceUnit),
      volumeUnit:      String(serverSettings.volume_unit ?? serverSettings.volumeUnit ?? f.volumeUnit),
    }));
  }, [serverSettings]);

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

  // ── About — live platform metadata from the API (no hardcoded build claims)
  const aboutQ = useQuery({ queryKey: ["about-platform"], queryFn: aboutApi.platform, enabled: section === "about" });
  const healthQ = useQuery({ queryKey: ["about-health"], queryFn: aboutApi.healthSummary, enabled: section === "about" });

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

  const SECTIONS: { key: Section; label: string; hint: string; icon: React.ElementType }[] = [
    { key: "organization",  label: "Organization",     hint: "Profile & contact",         icon: Building2 },
    { key: "preferences",   label: "Preferences",      hint: "Language, units, locale",   icon: Languages },
    { key: "notifications", label: "Notifications",    hint: "Channels per event",        icon: Bell },
    { key: "security",      label: "Security & Auth",  hint: "Password, MFA, SSO",        icon: Shield },
    { key: "api",           label: "API & Webhooks",   hint: "Keys & integrations",       icon: Webhook },
    { key: "about",         label: "About Platform",   hint: "Version & support",         icon: Info },
  ];

  function SelectField({ label, value, onChange, options }: { label: string; value: string; onChange: (v: string) => void; options: string[] }) {
    return (
      <div className="grid grid-cols-[1fr_1.5fr] items-center gap-4 border-b border-slate-100 pb-3 min-w-0">
        <label className="text-sm text-slate-600 truncate" title={label}>{label}</label>
        <select aria-label={label} className="field w-full min-w-0" value={value} onChange={(e) => onChange(e.target.value)} disabled={!canUpdateSettings}>
          {options.map((o) => <option key={o} value={o}>{o}</option>)}
        </select>
      </div>
    );
  }

  function TextField({ label, value, onChange, type = "text", placeholder }: { label: string; value: string; onChange: (v: string) => void; type?: string; placeholder?: string }) {
    return (
      <div className="grid grid-cols-[1fr_1.5fr] items-center gap-4 border-b border-slate-100 pb-3 min-w-0">
        <label className="text-sm text-slate-600 truncate" title={label}>{label}</label>
        <input aria-label={label} type={type} className="field w-full min-w-0" value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} disabled={!canUpdateSettings} />
      </div>
    );
  }

  const about = (aboutQ.data ?? null) as AnyRecord | null;
  const health = (healthQ.data ?? null) as AnyRecord | null;

  return (
    <div className="iam flex h-full flex-col gap-6 overflow-y-auto py-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-xl font-bold text-slate-900 flex items-center gap-2">
            <Settings className="h-5 w-5 text-slate-400 shrink-0" />{t("settings")}
          </h1>
          <p className="text-sm text-slate-500 mt-0.5">Workspace, security and integration controls for {String(session?.company?.name ?? "your organization")}</p>
        </div>
        <div className="iam-stat flex items-center gap-3 px-4 py-2">
          <span className="h-2 w-2 shrink-0 rounded-full bg-emerald-500 shadow-[0_0_6px_rgba(16,185,129,.8)]" />
          <div className="min-w-0 text-xs">
            <p className="font-bold text-slate-800 truncate">{String(session?.user?.fullName ?? session?.user?.email ?? "Signed in")}</p>
            <p className="text-slate-500 truncate">{String(session?.role ?? "")} · {String(session?.company?.name ?? "")}</p>
          </div>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[240px_1fr] items-start">
        {/* ── Left rail — settings IA ─────────────────────────────────────── */}
        <nav className="iam-rail flex lg:flex-col gap-1.5 overflow-x-auto lg:overflow-visible lg:sticky lg:top-4" aria-label="Settings sections">
          {SECTIONS.map(({ key, label, hint, icon: Icon }) => (
            <button
              type="button"
              key={key}
              onClick={() => setSection(key)}
              data-active={section === key}
              className="iam-rail-item shrink-0 lg:shrink"
            >
              <Icon className="h-4 w-4 shrink-0" />
              <span className="min-w-0">
                <span className="block truncate">{label}</span>
                <span className="hidden lg:block text-[10px] font-medium text-slate-400 truncate">{hint}</span>
              </span>
            </button>
          ))}
        </nav>

        <div className="min-w-0 flex flex-col gap-5">
          {/* ── ORGANIZATION ─────────────────────────────────────────────── */}
          {section === "organization" && (
            <div className="iam-card space-y-4 p-6">
              <SectionHeader icon={Building2} title="Company Profile" description="Display name, contact info and business address — shown on documents and the customer portal" />
              <div className="grid gap-x-8 gap-y-3 xl:grid-cols-2">
                <div className="space-y-3 min-w-0">
                  <TextField label="Company Name"    value={companyForm.displayName}  onChange={(v) => setCompanyForm((f) => ({ ...f, displayName: v }))} />
                  <TextField label="Address"         value={companyForm.addressLine1} onChange={(v) => setCompanyForm((f) => ({ ...f, addressLine1: v }))} />
                  <div className="grid grid-cols-3 gap-3">
                    <div className="min-w-0">
                      <label className="text-xs text-slate-500">City</label>
                      <input aria-label="City" className="field mt-1 w-full" value={companyForm.city} onChange={(e) => setCompanyForm((f) => ({ ...f, city: e.target.value }))} disabled={!canUpdateSettings} />
                    </div>
                    <div className="min-w-0">
                      <label className="text-xs text-slate-500">State / Region</label>
                      <input aria-label="State / Region" className="field mt-1 w-full" value={companyForm.state} onChange={(e) => setCompanyForm((f) => ({ ...f, state: e.target.value }))} disabled={!canUpdateSettings} />
                    </div>
                    <div className="min-w-0">
                      <label className="text-xs text-slate-500">Country</label>
                      <select aria-label="Country" className="field mt-1 w-full" value={companyForm.country} onChange={(e) => setCompanyForm((f) => ({ ...f, country: e.target.value }))} disabled={!canUpdateSettings}>
                        {["US","CA","SA","AE","PK","GB"].map((c) => <option key={c}>{c}</option>)}
                      </select>
                    </div>
                  </div>
                </div>
                <div className="space-y-3 min-w-0">
                  <TextField label="Phone"    value={companyForm.phone}        onChange={(v) => setCompanyForm((f) => ({ ...f, phone: v }))}        type="tel" />
                  <TextField label="Email"    value={companyForm.contactEmail} onChange={(v) => setCompanyForm((f) => ({ ...f, contactEmail: v }))} type="email" />
                  <TextField label="Website"  value={companyForm.website}      onChange={(v) => setCompanyForm((f) => ({ ...f, website: v }))} />
                </div>
              </div>
              <SaveRow onSave={saveCompany} isPending={companyProfileMut.isPending} saved={companySaved} canSave={canUpdateSettings} />
            </div>
          )}

          {/* ── PREFERENCES ──────────────────────────────────────────────── */}
          {section === "preferences" && (
            <div className="grid gap-5 xl:grid-cols-2 items-start">
              <div className="iam-card space-y-4 p-6">
                <SectionHeader icon={Languages} title={t("language")} description="Interface language and text direction" />
                <p className="text-xs text-slate-500 -mt-2">{t("language_rtl_note")}</p>
                <div className="grid grid-cols-2 gap-2">
                  {(Object.entries(LOCALES) as [LocaleCode, typeof LOCALES[LocaleCode]][]).map(([code, meta]) => (
                    <button
                      type="button"
                      key={code}
                      onClick={() => { setLocale(code); setLocaleForm((f) => ({ ...f, defaultLanguage: code })); }}
                      className={`relative flex min-w-0 flex-col items-start rounded-xl border p-3 text-start transition-all ${locale === code ? "border-teal-300 bg-teal-50/70 shadow-[inset_2px_2px_5px_rgba(13,148,136,.10),inset_-3px_-3px_7px_rgba(255,255,255,.9)]" : "border-slate-200 bg-white hover:border-slate-300 shadow-[-2px_-2px_5px_rgba(255,255,255,.9),3px_4px_8px_rgba(141,157,184,.18)]"}`}
                    >
                      {locale === code && (
                        <span className="absolute top-2 inset-e-2 h-4 w-4 rounded-full bg-teal-500 flex items-center justify-center">
                          <Check className="h-2.5 w-2.5 text-white" />
                        </span>
                      )}
                      <p className="text-xs font-semibold text-slate-900 truncate w-full">{meta.nativeLabel}</p>
                      <p className="text-[10px] text-slate-500 mt-0.5 truncate w-full">{meta.label}</p>
                      {meta.rtl && <span className="mt-1 rounded border border-amber-400/30 bg-amber-400/10 px-1 py-0.5 text-[9px] font-bold text-amber-600">RTL</span>}
                    </button>
                  ))}
                </div>
                {isRtl && <div className="rounded-xl border border-emerald-400/20 bg-emerald-50 p-3 text-xs text-emerald-700">RTL layout is active.</div>}
              </div>

              <div className="iam-card space-y-4 p-6">
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
            </div>
          )}

          {/* ── NOTIFICATIONS ────────────────────────────────────────────── */}
          {section === "notifications" && (
            <div className="iam-card space-y-5 p-6">
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
                      <tr key={key} className="hover:bg-slate-50/60 transition">
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
                                label={`${label} via ${ch}`}
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

          {/* ── SECURITY & AUTH ──────────────────────────────────────────── */}
          {section === "security" && (
            <>
              <div className="grid gap-5 xl:grid-cols-2 items-start">
                {/* Self-service: any signed-in user can change their own password here,
                    regardless of role — no admin action and no SMTP required. */}
                <ChangePasswordCard />
                <div className="flex flex-col gap-5 min-w-0">
                  <div className="iam-card space-y-4 p-6">
                    <SectionHeader icon={Lock} title="Session & Access" description="Idle timeout and audit log retention policy" />
                    <div className="space-y-3">
                      <SelectField label="Session Timeout (min)" value={securityForm.sessionTimeoutMin} onChange={(v) => setSecurityForm((f) => ({ ...f, sessionTimeoutMin: v }))} options={["15","30","60","120","240"]} />
                      <TextField  label="Audit Retention (days)" value={securityForm.auditRetentionDays} onChange={(v) => setSecurityForm((f) => ({ ...f, auditRetentionDays: v }))} type="number" />
                    </div>
                    <div className="flex items-center justify-between gap-4 border-b border-slate-100 pb-3">
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-slate-800">Require 2FA for all users</p>
                        <p className="text-xs text-slate-500 mt-0.5">Every user must enrol a TOTP or email OTP before accessing the platform.</p>
                      </div>
                      <ToggleSwitch checked={securityForm.requireMfa} onChange={(v) => setSecurityForm((f) => ({ ...f, requireMfa: v }))} disabled={!canUpdateSettings} label="Require 2FA for all users" />
                    </div>
                    <SaveRow onSave={saveSecurity} isPending={securitySettingsMut.isPending} saved={securitySaved} canSave={canUpdateSettings} />
                  </div>

                  <div className="iam-card space-y-4 p-6">
                    <SectionHeader icon={Lock} title="Active Session" description="Details about your current authenticated session" />
                    <div className="grid grid-cols-2 gap-3 text-xs">
                      {[
                        ["User",     String(session?.user?.fullName ?? session?.user?.email ?? "—")],
                        ["Role",     String(session?.role ?? "—")],
                        ["Tenant",   String(session?.company?.name ?? session?.company?.id ?? "—")],
                        ["Session",  "Active · Server-verified"],
                      ].map(([k, v]) => (
                        <div key={k} className="iam-kv flex-col !items-start gap-1">
                          <p className="text-slate-500 font-medium">{k}</p>
                          <p className="text-slate-900 font-semibold break-words w-full">{v}</p>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              </div>

              <SsoConnectionsPanel />
            </>
          )}

          {/* ── API & WEBHOOKS ───────────────────────────────────────────── */}
          {section === "api" && (
            <div className="grid gap-5 xl:grid-cols-2 items-start">
              <div className="iam-card space-y-4 p-6">
                <SectionHeader icon={Key} title="API Keys" description="Server-to-server credentials, hashed at rest — the raw key is shown once" />

                {justCreatedKey && (
                  <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-2">
                    <p className="text-xs font-semibold text-amber-800">New key created — copy it now, it will not be shown again.</p>
                    <div className="flex items-center gap-2 min-w-0">
                      <code className="min-w-0 flex-1 rounded-lg border border-amber-200 bg-white px-3 py-2 text-xs font-mono text-slate-700 overflow-x-auto select-all">
                        {justCreatedKey.apiKey}
                      </code>
                      <button type="button" className="btn-secondary text-xs shrink-0" onClick={() => copyApiKey(justCreatedKey.apiKey)}>
                        {apiKeyCopied ? <><Check className="h-3.5 w-3.5" /> Copied</> : <><Copy className="h-3.5 w-3.5" /> Copy</>}
                      </button>
                    </div>
                    <div className="min-w-0">
                      <p className="text-xs text-slate-500 font-semibold mb-1.5">Authentication Header</p>
                      <code className="block rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs font-mono text-slate-600 overflow-x-auto">
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
                  <p className="rounded-lg border border-dashed border-slate-200 px-4 py-6 text-center text-xs text-slate-500">No API keys yet. Generate one to authenticate server-to-server calls.</p>
                ) : (
                  <div className="divide-y divide-slate-100 border border-slate-200 rounded-lg overflow-hidden">
                    {(apiKeysQ.data as AnyRecord[]).map((k) => (
                      <div key={String(k.id)} className="flex items-center justify-between gap-3 px-3 py-2.5 text-xs min-w-0">
                        <div className="min-w-0">
                          <p className="font-mono text-slate-700 truncate">{String(k.keyPrefix)}_••••{String(k.lastFour)}</p>
                          <p className="text-slate-400 mt-0.5 truncate">
                            {k.label ? `${k.label} · ` : ""}
                            Created {k.createdAt ? new Date(String(k.createdAt)).toLocaleDateString() : "—"}
                            {k.revokedAt ? " · Revoked" : ""}
                          </p>
                        </div>
                        {!k.revokedAt && canUpdateSettings && (
                          <button
                            type="button"
                            className="flex shrink-0 items-center gap-1 text-rose-600 hover:text-rose-700 font-semibold"
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
                    className="rounded-xl bg-teal-600 hover:bg-teal-500 text-white font-bold text-sm px-4 py-2 shadow-[0_3px_10px_rgba(13,148,136,.28)] transition active:translate-y-px disabled:opacity-50"
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

              <div className="iam-card space-y-4 p-6">
                <SectionHeader icon={Webhook} title="Webhooks" description="Configure the endpoint and events your integration subscribes to" />
                <div className="space-y-3">
                  <div className="min-w-0">
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
                          className={`rounded-full border px-2.5 py-1 text-xs font-medium transition max-w-full truncate ${webhookEvents.includes(evt) ? "border-teal-300 bg-teal-50 text-teal-700 shadow-[inset_2px_2px_5px_rgba(13,148,136,.10)]" : "border-slate-200 bg-white text-slate-500 hover:text-slate-700 shadow-[-2px_-2px_5px_rgba(255,255,255,.9),3px_4px_8px_rgba(141,157,184,.16)]"}`}
                        >
                          {evt}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="rounded-lg bg-slate-50 border border-slate-200 p-3 flex items-center justify-between gap-3 min-w-0">
                  <div className="text-xs text-slate-600 min-w-0">
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

          {/* ── ABOUT — live platform metadata, no hardcoded build claims ── */}
          {section === "about" && (
            <div className="iam-card space-y-4 p-6">
              <SectionHeader icon={Info} title="About OpsTrax" description="Platform version, environment and support — served live by the API" />
              {aboutQ.isLoading ? (
                <p className="text-xs text-slate-400">Loading platform info…</p>
              ) : aboutQ.isError ? (
                <p className="text-xs text-rose-600">Could not load platform info from the API.</p>
              ) : about && (
                <>
                  <div className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                    {[
                      ["Product",     String(about.shortName ?? "OpsTrax")],
                      ["Version",     String(about.version ?? "—")],
                      ["Environment", String(about.environment ?? "—")],
                      ["Developer",   String(about.developer ?? "—")],
                    ].map(([k, v]) => (
                      <div key={k} className="iam-stat">
                        <p className="text-[10px] text-slate-500 uppercase tracking-widest truncate">{k}</p>
                        <p className="font-bold text-slate-800 mt-1 text-sm break-words">{v}</p>
                      </div>
                    ))}
                  </div>
                  {health && (
                    <div className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                      {(Object.entries(health) as [string, unknown][]).slice(0, 4).map(([k, v]) => (
                        <div key={k} className="iam-kv flex-col !items-start gap-1">
                          <p className="text-[10px] text-slate-500 uppercase tracking-widest truncate w-full">{k.replace(/([A-Z])/g, " $1")}</p>
                          <p className="text-xs font-semibold text-slate-800 break-words w-full">{String(v ?? "—")}</p>
                        </div>
                      ))}
                    </div>
                  )}
                  <div className="border-t border-slate-200 pt-3 space-y-1.5">
                    <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Support</p>
                    <a href={`https://${String((about.support as AnyRecord | undefined)?.website ?? "www.kodekinetics.com")}`} target="_blank" rel="noopener noreferrer" className="flex items-center gap-2 text-xs text-teal-600 hover:text-teal-500 transition">
                      <Globe className="h-3.5 w-3.5 shrink-0" /><span className="truncate">{String((about.support as AnyRecord | undefined)?.website ?? "")}</span><ExternalLink className="h-3 w-3 opacity-60 shrink-0" />
                    </a>
                    <a href={`mailto:${String((about.support as AnyRecord | undefined)?.email ?? "")}`} className="flex items-center gap-2 text-xs text-slate-500 hover:text-slate-700 transition">
                      <Mail className="h-3.5 w-3.5 shrink-0" /><span className="truncate">{String((about.support as AnyRecord | undefined)?.email ?? "")}</span>
                    </a>
                    <a href={`tel:${String((about.support as AnyRecord | undefined)?.phone ?? "")}`} className="flex items-center gap-2 text-xs text-slate-500 hover:text-slate-700 transition">
                      <Phone className="h-3.5 w-3.5 shrink-0" /><span className="truncate">{String((about.support as AnyRecord | undefined)?.phone ?? "")}</span>
                    </a>
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
