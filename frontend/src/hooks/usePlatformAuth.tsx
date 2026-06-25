import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import {
  loadPlatformSession,
  storePlatformSession,
  platformApi,
  hasPlatformPermission,
  type PlatformSession,
} from "@/services/platformApi";

type PlatformAuthValue = {
  session: PlatformSession | null;
  setSession: (s: PlatformSession | null) => void;
  logout: () => Promise<void>;
  can: (permission: string) => boolean;
};

const PlatformAuthContext = createContext<PlatformAuthValue | null>(null);

export function PlatformAuthProvider({ children }: { children: ReactNode }) {
  const [session, setSessionState] = useState<PlatformSession | null>(loadPlatformSession);

  const setSession = (next: PlatformSession | null) => {
    setSessionState(next);
    storePlatformSession(next);
  };

  const value = useMemo<PlatformAuthValue>(
    () => ({
      session,
      setSession,
      logout: async () => {
        try {
          await platformApi.logout();
        } finally {
          setSession(null);
        }
      },
      can: (permission: string) => hasPlatformPermission(session?.permissions ?? [], permission),
    }),
    [session],
  );

  return <PlatformAuthContext.Provider value={value}>{children}</PlatformAuthContext.Provider>;
}

export function usePlatformAuth() {
  const value = useContext(PlatformAuthContext);
  if (!value) throw new Error("usePlatformAuth must be used inside PlatformAuthProvider");
  return value;
}
