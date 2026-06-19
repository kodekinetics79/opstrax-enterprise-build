import { useLocale } from '../contexts/LocaleContext';

/**
 * Convenience hook: returns the `t` translator from LocaleContext.
 * Use instead of `const { t } = useLocale()` for a shorter import.
 *
 * @example
 *   const t = useT();
 *   <h1>{t('Leave & Absence Management')}</h1>
 */
export function useT() {
  const { t } = useLocale();
  return t;
}
