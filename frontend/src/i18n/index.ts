import { createContext, useContext } from "react";
import type { I18nKeys } from "./locales/en-US";
import enUS from "./locales/en-US";
import enCA from "./locales/en-CA";
import frCA from "./locales/fr-CA";
import arSA from "./locales/ar-SA";
import arAE from "./locales/ar-AE";
import urPK from "./locales/ur-PK";

export type { I18nKeys };

export const LOCALES = {
  "en-US": { label: "English (US)",           nativeLabel: "English (US)",           dict: enUS, rtl: false },
  "en-CA": { label: "English (Canada)",        nativeLabel: "English (Canada)",        dict: enCA, rtl: false },
  "fr-CA": { label: "French (Canada)",         nativeLabel: "Français (Canada)",       dict: frCA, rtl: false },
  "ar-SA": { label: "Arabic (Saudi Arabia)",   nativeLabel: "العربية (السعودية)",      dict: arSA, rtl: true  },
  "ar-AE": { label: "Arabic (UAE)",            nativeLabel: "العربية (الإمارات)",      dict: arAE, rtl: true  },
  "ur-PK": { label: "Urdu (Pakistan)",         nativeLabel: "اردو (پاکستان)",          dict: urPK, rtl: true  },
} as const;

export type LocaleCode = keyof typeof LOCALES;

export interface I18nContextValue {
  locale: LocaleCode;
  setLocale: (code: LocaleCode) => void;
  t: (key: I18nKeys) => string;
  isRtl: boolean;
}

export const I18nContext = createContext<I18nContextValue>({
  locale: "en-US",
  setLocale: () => {},
  t: (key) => enUS[key] as string,
  isRtl: false,
});

export function useI18n() {
  return useContext(I18nContext);
}
