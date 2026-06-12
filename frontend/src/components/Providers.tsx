'use client';

import { AuthProvider } from '@/src/contexts/AuthContext';
import { FeatureFlagProvider } from '@/src/contexts/FeatureFlagContext';
import { HelpTextProvider } from '@/src/contexts/HelpTextContext';

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <AuthProvider>
      <FeatureFlagProvider>
        <HelpTextProvider>{children}</HelpTextProvider>
      </FeatureFlagProvider>
    </AuthProvider>
  );
}
