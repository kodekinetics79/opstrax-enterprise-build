'use client';

import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { LOCALE_DICTS, LOCALE_METADATA, translate } from '../i18n/translations';
import type { LocaleCode } from '../i18n/translations';

export { type LocaleCode };
export const LOCALES = Object.entries(LOCALE_METADATA).map(([code, meta]) => ({
  code: code as LocaleCode,
  ...meta,
}));

const STORAGE_KEY = 'kynexone-locale';

function readStoredLocale(): LocaleCode {
  if (typeof window === 'undefined') return 'en';
  const v = localStorage.getItem(STORAGE_KEY);
  return (v && v in LOCALE_DICTS ? v : 'en') as LocaleCode;
}

function applyLocale(code: LocaleCode) {
  const meta = LOCALE_METADATA[code];
  document.documentElement.dir = meta.dir;
  document.documentElement.lang = code;
  localStorage.setItem(STORAGE_KEY, code);
}

interface LocaleCtx {
  locale: LocaleCode;
  dir: 'ltr' | 'rtl';
  setLocale: (code: LocaleCode) => void;
  t: (key: string) => string;
}

const Ctx = createContext<LocaleCtx>({
  locale: 'en',
  dir: 'ltr',
  setLocale: () => {},
  t: (k) => k,
});

export function LocaleProvider({ children }: { children: React.ReactNode }) {
  const [locale, setLocaleState] = useState<LocaleCode>('en');

  // Sync from localStorage once on mount (avoid SSR mismatch)
  useEffect(() => {
    const stored = readStoredLocale();
    setLocaleState(stored);
    applyLocale(stored);
  }, []);

  const setLocale = useCallback((code: LocaleCode) => {
    setLocaleState(code);
    applyLocale(code);
  }, []);

  const t = useCallback((key: string) => translate(locale, key), [locale]);

  return (
    <Ctx.Provider value={{ locale, dir: LOCALE_METADATA[locale].dir, setLocale, t }}>
      {children}
    </Ctx.Provider>
  );
}

export function useLocale() {
  return useContext(Ctx);
}
