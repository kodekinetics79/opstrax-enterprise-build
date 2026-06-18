import { useEffect, useRef, useState } from 'react';

const DEBOUNCE_MS = 700;

/**
 * Debounced English → Arabic auto-translation via MyMemory (free, no key required).
 * Returns the latest translated text and a loading flag.
 * Aborts in-flight requests when the source changes or the component unmounts.
 */
export function useAutoTranslate(source: string) {
  const [translation, setTranslation] = useState('');
  const [isTranslating, setIsTranslating] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const trimmed = source.trim();
    if (!trimmed || trimmed.length < 2) {
      setTranslation('');
      return;
    }

    const timer = setTimeout(async () => {
      abortRef.current?.abort();
      abortRef.current = new AbortController();
      setIsTranslating(true);

      try {
        const res = await fetch(
          `https://api.mymemory.translated.net/get?q=${encodeURIComponent(trimmed)}&langpair=en|ar`,
          { signal: abortRef.current.signal },
        );
        const data: { responseStatus: number; responseData?: { translatedText: string } } = await res.json();
        if (data.responseStatus === 200 && data.responseData?.translatedText) {
          setTranslation(data.responseData.translatedText);
        }
      } catch {
        // Translation is best-effort — silently ignore network / abort errors
      } finally {
        setIsTranslating(false);
      }
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [source]);

  return { translation, isTranslating };
}
