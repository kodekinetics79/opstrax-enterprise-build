import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { flushSync } from "react-dom";
import { useQueryClient } from "@tanstack/react-query";
import {
  loadPlatformSession,
  storePlatformSession,
  platformApi,
  PLATFORM_STORAGE_KEY,
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
  const current = loadPlatformSession();
  if (!current?.token) throw new Error("Platform session is unavailable");
  const fresh = await platformApi.me();
  return { ...fresh, token: current.token };
}

export function PlatformAuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const initialRef = useRef<PlatformSession | null | undefined>(undefined);
  if (initialRef.current === undefined) initialRef.current = loadPlatformSession();
  const [session, setSessionState] = useState<PlatformSession | null>(initialRef.current);
  const [revalidating, setRevalidating] = useState(initialRef.current != null);

  const setSession = (next: PlatformSession | null) => {
    setSessionState(next);
    storePlatformSession(next);
    if (!next) queryClient.clear();
  };

  useEffect(() => {
    if (!initialRef.current) { setRevalidating(false); return; }
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
    const onStorage = (event: StorageEvent) => {
      if (event.key !== PLATFORM_STORAGE_KEY) return;
      if (!event.newValue) { setSession(null); return; }
      // Do not rewrite the storage value received from another tab: doing so
      // would create an endless cross-tab validation/write loop.
      revalidatePlatformSession().then((fresh) => { queryClient.clear(); setSessionState(fresh); }).catch(() => setSession(null));
    };
    window.addEventListener("pagehide", onPageHide);
    window.addEventListener("pageshow", onPageShow);
    window.addEventListener("storage", onStorage);
    return () => {
      window.removeEventListener("pagehide", onPageHide);
      window.removeEventListener("pageshow", onPageShow);
      window.removeEventListener("storage", onStorage);
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
