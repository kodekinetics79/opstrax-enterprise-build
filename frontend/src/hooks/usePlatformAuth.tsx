import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { flushSync } from "react-dom";
import { useQueryClient } from "@tanstack/react-query";
import {
  clearRetiredPlatformSession,
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

async function revalidatePlatformSession(): Promise<PlatformSession> {
  return platformApi.me();
}

export function PlatformAuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSessionState] = useState<PlatformSession | null>(null);
  // The browser cannot inspect an HttpOnly cookie, so every document starts by
  // asking /me whether a platform session exists. This also refreshes permissions.
  const [revalidating, setRevalidating] = useState(true);

  const setSession = (next: PlatformSession | null) => {
    setSessionState(next);
    if (!next) queryClient.clear();
  };

  useEffect(() => {
    clearRetiredPlatformSession();
    let cancelled = false;
    revalidatePlatformSession()
      .then((fresh) => { if (!cancelled) setSession(fresh); })
      .catch(() => { if (!cancelled) setSession(null); })
      .finally(() => { if (!cancelled) setRevalidating(false); });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let restoring = false;
    const onPageHide = (event: PageTransitionEvent) => {
      if (event.persisted) document.documentElement.style.visibility = "hidden";
    };
    const onPageShow = (event: PageTransitionEvent) => {
      if (!event.persisted || restoring) return;
      restoring = true;
      revalidatePlatformSession()
        .then((fresh) => {
          queryClient.clear();
          flushSync(() => setSession(fresh));
        })
        .catch(() => flushSync(() => setSession(null)))
        .finally(() => {
          restoring = false;
          document.documentElement.style.visibility = "";
        });
    };
    window.addEventListener("pagehide", onPageHide);
    window.addEventListener("pageshow", onPageShow);
    return () => {
      window.removeEventListener("pagehide", onPageHide);
      window.removeEventListener("pageshow", onPageShow);
      document.documentElement.style.visibility = "";
    };
  }, []);

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

  if (revalidating) return <div className="grid min-h-screen place-items-center bg-slate-950 text-sm text-slate-300">Validating Platform session…</div>;

  return <PlatformAuthContext.Provider value={value}>{children}</PlatformAuthContext.Provider>;
}

export function usePlatformAuth() {
  const value = useContext(PlatformAuthContext);
  if (!value) throw new Error("usePlatformAuth must be used inside PlatformAuthProvider");
  return value;
}
