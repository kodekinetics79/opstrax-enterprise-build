import { useState } from "react";
import { Check, Code2, ExternalLink, Globe, Languages, Mail, Phone, Settings } from "lucide-react";
import { useLocalizationSettings, useUpdateLocaleSettings, useUpdateUserPreferences } from "@/hooks/useBatch6";
import { useI18n, LOCALES } from "@/i18n";
import type { LocaleCode } from "@/i18n";
import type { AnyRecord } from "@/types";

const DATE_FORMATS = ["MM/DD/YYYY", "DD/MM/YYYY", "YYYY-MM-DD"];
const TIMEZONES = [
  "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
  "America/Toronto", "Europe/London", "Asia/Riyadh", "Asia/Dubai", "Asia/Karachi",
];
const CURRENCIES = ["USD", "CAD", "SAR", "AED", "PKR", "EUR", "GBP"];
const DISTANCE_UNITS = ["Miles", "Kilometers"];
const VOLUME_UNITS = ["Gallons", "Liters"];

export function SettingsPage() {
  const { t, locale, setLocale, isRtl } = useI18n();

  const settingsQ         = useLocalizationSettings();
  const updateSettingsMut = useUpdateLocaleSettings();
  const updatePrefsMut    = useUpdateUserPreferences();

  const serverSettings = ((settingsQ.data as AnyRecord[] | undefined)?.[0]) as AnyRecord | undefined;

  const [form, setForm] = useState({
    defaultLanguage: "en-US",
    defaultCountry:  "US",
    timezone:        "America/New_York",
    dateFormat:      "MM/DD/YYYY",
    currency:        "USD",
    distanceUnit:    "Miles",
    volumeUnit:      "Gallons",
  });

  const [saved, setSaved] = useState(false);

  function handleSave() {
    updateSettingsMut.mutate({
      defaultLanguage: form.defaultLanguage,
      defaultCountry:  form.defaultCountry,
      timezone:        form.timezone,
      dateFormat:      form.dateFormat,
      currency:        form.currency,
      distanceUnit:    form.distanceUnit,
      volumeUnit:      form.volumeUnit,
    });
    updatePrefsMut.mutate({ language: form.defaultLanguage, countryCode: form.defaultCountry });
    setLocale(form.defaultLanguage as LocaleCode);
    setSaved(true);
    setTimeout(() => setSaved(false), 2500);
  }

  function Field({ label, children }: { label: string; children: React.ReactNode }) {
    return (
      <div className="grid grid-cols-[1fr_1.5fr] items-center gap-4 border-b border-white/[0.05] pb-3">
        <label className="text-sm text-slate-400">{label}</label>
        {children}
      </div>
    );
  }

  return (
    <div className="space-y-6 max-w-2xl">
      {/* Header */}
      <div>
        <h1 className="text-xl font-extrabold text-white flex items-center gap-2">
          <Settings className="h-5 w-5 text-slate-400" />{t("settings")}
        </h1>
        <p className="text-xs text-slate-500 mt-0.5">Language, localization, and display preferences</p>
      </div>

      {/* Language selector — immediate UI preview */}
      <div className="panel space-y-4">
        <p className="section-title flex items-center gap-2"><Languages className="h-3.5 w-3.5 text-violet-400" />{t("language")}</p>
        <p className="text-xs text-slate-500">{t("language_rtl_note")}</p>

        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
          {(Object.entries(LOCALES) as [LocaleCode, typeof LOCALES[LocaleCode]][]).map(([code, meta]) => (
            <button
              key={code}
              onClick={() => { setLocale(code); setForm(f => ({ ...f, defaultLanguage: code })); }}
              className={`relative flex flex-col items-start rounded-xl border p-3 text-start transition-all ${
                locale === code
                  ? "border-violet-400/40 bg-violet-400/10"
                  : "border-white/[0.07] bg-white/[0.02] hover:border-white/20"
              }`}
            >
              {locale === code && (
                <span className="absolute top-2 end-2 h-4 w-4 rounded-full bg-violet-400 flex items-center justify-center">
                  <Check className="h-2.5 w-2.5 text-slate-950" />
                </span>
              )}
              <p className="text-xs font-semibold text-white">{meta.nativeLabel}</p>
              <p className="text-[10px] text-slate-500 mt-0.5">{meta.label}</p>
              {meta.rtl && <span className="mt-1 rounded border border-amber-400/20 bg-amber-400/10 px-1 py-0.5 text-[9px] font-bold text-amber-400">RTL</span>}
            </button>
          ))}
        </div>

        {isRtl && (
          <div className="rounded-xl border border-emerald-400/20 bg-emerald-400/5 p-3 text-xs text-emerald-200/80">
            RTL layout is active. The interface direction has been updated to right-to-left.
          </div>
        )}
      </div>

      {/* Locale settings */}
      <div className="panel space-y-4">
        <p className="section-title flex items-center gap-2"><Globe className="h-3.5 w-3.5 text-teal-400" />{t("tenant_settings")}</p>

        <div className="space-y-3">
          <Field label={t("default_country")}>
            <select className="field w-full" value={form.defaultCountry} onChange={e => setForm(f => ({ ...f, defaultCountry: e.target.value }))}>
              {["US","CA","SA","AE","PK"].map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </Field>

          <Field label={t("timezone")}>
            <select className="field w-full" value={form.timezone} onChange={e => setForm(f => ({ ...f, timezone: e.target.value }))}>
              {TIMEZONES.map(tz => <option key={tz} value={tz}>{tz}</option>)}
            </select>
          </Field>

          <Field label={t("date_format")}>
            <select className="field w-full" value={form.dateFormat} onChange={e => setForm(f => ({ ...f, dateFormat: e.target.value }))}>
              {DATE_FORMATS.map(f => <option key={f} value={f}>{f}</option>)}
            </select>
          </Field>

          <Field label={t("currency")}>
            <select className="field w-full" value={form.currency} onChange={e => setForm(f => ({ ...f, currency: e.target.value }))}>
              {CURRENCIES.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </Field>

          <Field label={t("distance_unit")}>
            <select className="field w-full" value={form.distanceUnit} onChange={e => setForm(f => ({ ...f, distanceUnit: e.target.value }))}>
              {DISTANCE_UNITS.map(u => <option key={u} value={u}>{u}</option>)}
            </select>
          </Field>

          <Field label={t("volume_unit")}>
            <select className="field w-full" value={form.volumeUnit} onChange={e => setForm(f => ({ ...f, volumeUnit: e.target.value }))}>
              {VOLUME_UNITS.map(u => <option key={u} value={u}>{u}</option>)}
            </select>
          </Field>
        </div>

        <div className="flex items-center gap-3 pt-2">
          <button
            className="rounded-xl bg-teal-500 hover:bg-teal-400 text-slate-950 font-bold text-sm px-5 py-2 transition"
            onClick={handleSave}
            disabled={updateSettingsMut.isPending}
          >
            {updateSettingsMut.isPending ? "Saving…" : t("save_settings")}
          </button>
          {saved && (
            <span className="flex items-center gap-1.5 text-xs text-emerald-400 font-semibold">
              <Check className="h-3.5 w-3.5" />{t("settings_saved")}
            </span>
          )}
        </div>
      </div>

      {/* Server locale state preview */}
      {serverSettings && (
        <div className="panel space-y-2">
          <p className="section-title text-slate-500">Current Server Settings</p>
          <div className="grid grid-cols-2 gap-x-6 gap-y-1 text-xs">
            {(Object.entries(serverSettings) as [string, unknown][])
              .filter(([k]) => !["id","tenant_id","created_at","updated_at"].includes(k))
              .map(([k, v]) => (
                <div key={k} className="flex justify-between border-b border-white/[0.04] py-1">
                  <span className="text-slate-500">{k.replace(/_/g, " ")}</span>
                  <span className="text-slate-300 font-mono">{String(v ?? "—")}</span>
                </div>
              ))}
          </div>
        </div>
      )}

      {/* Platform / About */}
      <div className="panel space-y-4">
        <p className="section-title flex items-center gap-2">
          <Code2 className="h-3.5 w-3.5 text-teal-400" />Platform &amp; Developer Info
        </p>
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
              <p className="font-semibold text-slate-300 mt-0.5 text-xs">{v}</p>
            </div>
          ))}
        </div>
        <div className="border-t border-white/[0.06] pt-3 space-y-1.5">
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Support &amp; Contact</p>
          <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer"
             className="flex items-center gap-2 text-xs text-teal-400 hover:text-teal-300 transition">
            <Globe className="h-3.5 w-3.5" />www.kodekinetics.com<ExternalLink className="h-3 w-3 opacity-60" />
          </a>
          <a href="mailto:info@kodekinetics.com"
             className="flex items-center gap-2 text-xs text-slate-400 hover:text-slate-300 transition">
            <Mail className="h-3.5 w-3.5" />info@kodekinetics.com
          </a>
          <a href="tel:+15714305333"
             className="flex items-center gap-2 text-xs text-slate-400 hover:text-slate-300 transition">
            <Phone className="h-3.5 w-3.5" />+1 571 430 5333
          </a>
        </div>
        <div className="rounded-lg border border-amber-400/15 bg-amber-400/5 p-3">
          <p className="text-xs text-amber-200/80 leading-relaxed">
            OpsTrax provides compliance management, monitoring, and audit-readiness tools.
            Final regulatory compliance remains the carrier&apos;s responsibility. ELD certification
            depends on the connected ELD provider/device and applicable country requirements.
          </p>
        </div>
      </div>
    </div>
  );
}
