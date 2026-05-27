import { useEffect, useRef, useState } from "react";
import { I18nContext, LOCALES } from "./index";
import type { I18nKeys, LocaleCode } from "./index";
import { apiClient } from "@/services/apiClient";
import type { ApiEnvelope, AnyRecord } from "@/types";

const STORAGE_KEY = "opstrax_locale";

function getInitialLocale(): LocaleCode {
  const stored = localStorage.getItem(STORAGE_KEY) as LocaleCode | null;
  return stored && stored in LOCALES ? stored : "en-US";
}

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [locale, setLocaleState] = useState<LocaleCode>(getInitialLocale);
  const initialized = useRef(false);

  // On mount: sync locale from API (overrides localStorage if server has a newer preference)
  useEffect(() => {
    if (initialized.current) return;
    initialized.current = true;
    apiClient
      .get<ApiEnvelope<AnyRecord[]>>("/api/localization/user-preferences")
      .then((res) => {
        const lang = String(res.data?.data?.[0]?.language ?? "");
        if (lang && lang in LOCALES) {
          setLocaleState(lang as LocaleCode);
          localStorage.setItem(STORAGE_KEY, lang);
        }
      })
      .catch(() => {});  // silent — localStorage is the fallback
  }, []);

  const setLocale = (code: LocaleCode) => {
    setLocaleState(code);
    localStorage.setItem(STORAGE_KEY, code);
    // Persist to API fire-and-forget; localStorage keeps UI in sync immediately
    apiClient
      .put("/api/localization/user-preferences", {
        language: code,
        country_code: code.split("-")[1] ?? "US",
        timezone: Intl.DateTimeFormat().resolvedOptions().timeZone ?? "UTC",
        date_format: "MM/dd/yyyy",
      })
      .catch(() => {});
  };

  useEffect(() => {
    const { rtl } = LOCALES[locale];
    document.documentElement.dir = rtl ? "rtl" : "ltr";
    document.documentElement.lang = locale;
  }, [locale]);

  const { dict, rtl } = LOCALES[locale];

  function t(key: I18nKeys): string {
    return (dict as Record<string, string>)[key] ?? key;
  }

  return (
    <I18nContext.Provider value={{ locale, setLocale, t, isRtl: rtl }}>
      {children}
    </I18nContext.Provider>
  );
}
