'use client';

import { useEffect, useRef, useState } from 'react';
import type { LocaleCode } from '../i18n/translations';

const DEBOUNCE_MS = 600;
const CACHE_PREFIX = 'kynexone-tx-';

// MyMemory language pair codes
const LANG_PAIR: Partial<Record<LocaleCode, string>> = {
  ar: 'en|ar',
  fr: 'en|fr',
  es: 'en|es',
};

function cacheKey(locale: string, source: string): string {
  return `${CACHE_PREFIX}${locale}:${source.trim().toLowerCase()}`;
}

function getCache(locale: string, source: string): string | null {
  if (typeof window === 'undefined') return null;
  try { return localStorage.getItem(cacheKey(locale, source)); } catch { return null; }
}

function setCache(locale: string, source: string, translation: string): void {
  if (typeof window === 'undefined') return;
  try { localStorage.setItem(cacheKey(locale, source), translation); } catch { /* quota */ }
}

/**
 * Debounced English → target-locale auto-translation via MyMemory (free, no key required).
 * Results are cached in localStorage to avoid repeat API calls.
 * Falls back silently on network errors — translation is best-effort.
 */
export function useAutoTranslate(source: string, locale: LocaleCode = 'ar') {
  const [translation, setTranslation] = useState('');
  const [isTranslating, setIsTranslating] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const trimmed = source.trim();

    // English or unsupported locale — no translation needed
    if (!trimmed || trimmed.length < 2 || locale === 'en') {
      setTranslation('');
      return;
    }

    const pair = LANG_PAIR[locale];
    if (!pair) { setTranslation(''); return; }

    // Serve from cache immediately
    const cached = getCache(locale, trimmed);
    if (cached) {
      setTranslation(cached);
      return;
    }

    const timer = setTimeout(async () => {
      abortRef.current?.abort();
      abortRef.current = new AbortController();
      setIsTranslating(true);

      try {
        const res = await fetch(
          `https://api.mymemory.translated.net/get?q=${encodeURIComponent(trimmed)}&langpair=${pair}`,
          { signal: abortRef.current.signal },
        );
        const data: { responseStatus: number; responseData?: { translatedText: string } } = await res.json();
        if (data.responseStatus === 200 && data.responseData?.translatedText) {
          const tx = data.responseData.translatedText;
          setCache(locale, trimmed, tx);
          setTranslation(tx);
        }
      } catch {
        // Best-effort — silently ignore network / abort errors
      } finally {
        setIsTranslating(false);
      }
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [source, locale]);

  return { translation, isTranslating };
}
