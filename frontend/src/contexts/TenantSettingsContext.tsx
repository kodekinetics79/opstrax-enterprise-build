'use client';

import { createContext, useContext, useEffect, useState, useCallback } from 'react';
import client from '../api/client';

export interface TenantSettings {
  currencyCode: string;
  countryCode: string;
  defaultTimezone: string;
  dateFormat: string;
  workWeek: string;
  weekStartDay: string;
  defaultLanguage: string;
  rtlEnabled: boolean;
  calendarSystem: string;
  hijriDatesEnabled: boolean;
}

const DEFAULTS: TenantSettings = {
  currencyCode: 'USD',
  countryCode: 'US',
  defaultTimezone: 'America/New_York',
  dateFormat: 'MM/DD/YYYY',
  workWeek: 'Mon-Fri',
  weekStartDay: 'Monday',
  defaultLanguage: 'en',
  rtlEnabled: false,
  calendarSystem: 'Gregorian',
  hijriDatesEnabled: false,
};

interface TenantSettingsContextValue {
  settings: TenantSettings;
  reload: () => Promise<void>;
}

const TenantSettingsContext = createContext<TenantSettingsContextValue>({
  settings: DEFAULTS,
  reload: async () => {},
});

export function TenantSettingsProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = useState<TenantSettings>(DEFAULTS);

  const load = useCallback(async () => {
    try {
      const { data } = await client.get<Partial<TenantSettings>>('/api/tenant-admin/localization');
      if (data) {
        setSettings({
          currencyCode: data.currencyCode || DEFAULTS.currencyCode,
          countryCode: data.countryCode || DEFAULTS.countryCode,
          defaultTimezone: data.defaultTimezone || DEFAULTS.defaultTimezone,
          dateFormat: data.dateFormat || DEFAULTS.dateFormat,
          workWeek: data.workWeek || DEFAULTS.workWeek,
          weekStartDay: data.weekStartDay || DEFAULTS.weekStartDay,
          defaultLanguage: data.defaultLanguage || DEFAULTS.defaultLanguage,
          rtlEnabled: data.rtlEnabled ?? DEFAULTS.rtlEnabled,
          calendarSystem: data.calendarSystem || DEFAULTS.calendarSystem,
          hijriDatesEnabled: data.hijriDatesEnabled ?? DEFAULTS.hijriDatesEnabled,
        });
      }
    } catch {
      // Fail silently — use defaults. Happens on first load before auth token is set.
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  return (
    <TenantSettingsContext.Provider value={{ settings, reload: load }}>
      {children}
    </TenantSettingsContext.Provider>
  );
}

export function useTenantSettings(): TenantSettings {
  return useContext(TenantSettingsContext).settings;
}

export function useTenantSettingsContext(): TenantSettingsContextValue {
  return useContext(TenantSettingsContext);
}
