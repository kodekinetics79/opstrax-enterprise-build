'use client';

import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { helpTextsApi } from '../api/helpTexts';

interface HelpTextContextValue {
  /** fieldKey → tenant-customized tooltip text */
  overrides: Record<string, string>;
  /** Re-fetch overrides (call after the admin edits them). */
  refresh: () => Promise<void>;
}

const HelpTextContext = createContext<HelpTextContextValue>({ overrides: {}, refresh: async () => {} });

export function HelpTextProvider({ children }: { children: React.ReactNode }) {
  const [overrides, setOverrides] = useState<Record<string, string>>({});

  const refresh = useCallback(async () => {
    // Only meaningful inside a tenant session; platform pages have no tenant token.
    if (typeof window === 'undefined' || !localStorage.getItem('zayra_access_token')) return;
    try {
      const items = await helpTextsApi.list();
      setOverrides(Object.fromEntries(items.map(i => [i.fieldKey, i.text])));
    } catch { /* tooltips fall back to built-in text */ }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  return <HelpTextContext.Provider value={{ overrides, refresh }}>{children}</HelpTextContext.Provider>;
}

export function useHelpTexts() {
  return useContext(HelpTextContext);
}
