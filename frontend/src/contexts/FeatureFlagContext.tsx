'use client';

import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { useAuth } from './AuthContext';
import { tenantAdminApi } from '../api/intelligence';

interface FeatureFlagContextValue {
  /**
   * Returns true when the feature is enabled for the current tenant.
   * Also returns true when flags have not loaded yet, or when the
   * current user cannot call the flags endpoint (fail-open so that
   * permission-gating in the sidebar still handles visibility).
   */
  isFeatureEnabled: (featureKey: string) => boolean;
}

const FeatureFlagContext = createContext<FeatureFlagContextValue>({
  isFeatureEnabled: () => true,
});

export function FeatureFlagProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  // null  → not yet loaded, or non-admin user (fail-open)
  // Record → loaded successfully
  const [flags, setFlags] = useState<Record<string, boolean> | null>(null);

  const refresh = useCallback(async () => {
    if (!user) {
      setFlags(null);
      return;
    }
    try {
      const list = await tenantAdminApi.listFeatureFlags();
      setFlags(Object.fromEntries(list.map((f) => [f.featureKey, f.isEnabled])));
    } catch {
      // 403 for non-admin roles, or network error → fail-open.
      // Backend feature-flag enforcement on each API route is the real gate.
      setFlags(null);
    }
  }, [user]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const isFeatureEnabled = useCallback(
    (featureKey: string): boolean => {
      if (flags === null) return true; // not loaded or non-admin → show all
      // A key that has never been toggled (undefined) is treated as enabled.
      return flags[featureKey] !== false;
    },
    [flags],
  );

  return (
    <FeatureFlagContext.Provider value={{ isFeatureEnabled }}>
      {children}
    </FeatureFlagContext.Provider>
  );
}

export function useFeatureFlags() {
  return useContext(FeatureFlagContext);
}
