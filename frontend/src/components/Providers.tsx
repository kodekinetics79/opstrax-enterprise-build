'use client';

import { AuthProvider } from '@/src/contexts/AuthContext';
import { FeatureFlagProvider } from '@/src/contexts/FeatureFlagContext';
import { HelpTextProvider } from '@/src/contexts/HelpTextContext';
import { AppToastProvider } from '@/src/components/ui/AppToast';

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <AppToastProvider>
      <AuthProvider>
        <FeatureFlagProvider>
          <HelpTextProvider>{children}</HelpTextProvider>
        </FeatureFlagProvider>
      </AuthProvider>
    </AppToastProvider>
  );
}
