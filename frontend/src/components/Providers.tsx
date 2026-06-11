'use client';

import { AuthProvider } from '@/src/contexts/AuthContext';
import { HelpTextProvider } from '@/src/contexts/HelpTextContext';

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <AuthProvider>
      <HelpTextProvider>{children}</HelpTextProvider>
    </AuthProvider>
  );
}
