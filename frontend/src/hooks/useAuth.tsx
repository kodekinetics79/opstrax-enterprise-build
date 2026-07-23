import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { UserSession } from "@/types";
import { authApi } from "@/services/authApi";
import { SESSION_STORAGE_KEY as STORAGE_KEY, RETIRED_SESSION_KEYS } from "@/auth/sessionStorage";

const SESSION_TTL_MS = 8 * 60 * 60 * 1000; // 8 hours

type StoredSession = { session: UserSession; expiresAt: number };

function loadSession(): UserSession | null {
  try {
    for (const k of RETIRED_SESSION_KEYS) localStorage.removeItem(k);
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const { session, expiresAt } = JSON.parse(raw) as StoredSession;
    if (Date.now() > expiresAt) {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return session;
  } catch {
    localStorage.removeItem(STORAGE_KEY);
    return null;
  }
}

type AuthContextValue = {
  session: UserSession | null;
  setSession: (session: UserSession | null) => void;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  // Compute the initial (possibly stale) session exactly once.
  const initialRef = useRef<UserSession | null | undefined>(undefined);
  if (initialRef.current === undefined) initialRef.current = loadSession();
  const [session, setSessionState] = useState<UserSession | null>(initialRef.current);

  // A stored session carries a PERMISSIONS SNAPSHOT taken at login. If the server has since changed
  // the user's grants (e.g. the Driver role was isolated), that snapshot is stale — and rendering
  // from it would show surfaces the server now forbids (a driver briefly seeing the admin shell before
  // every call 403s). So when a stored session exists we re-validate it against /api/auth/me BEFORE
  // rendering: /me returns a fresh session (same bearer token + CURRENT permissions/role). We block on
  // it once, so the app only ever renders from server-current permissions. No stored session → nothing
  // to revalidate (the login screen renders immediately).
  const [revalidating, setRevalidating] = useState<boolean>(initialRef.current != null);
  const didRevalidate = useRef(false);

  const setSession = (next: UserSession | null) => {
    setSessionState(next);
    if (next) {
      const stored: StoredSession = { session: next, expiresAt: Date.now() + SESSION_TTL_MS };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  };

  useEffect(() => {
    if (didRevalidate.current) return;
    didRevalidate.current = true;
    if (!initialRef.current) { setRevalidating(false); return; }
    let cancelled = false;
    authApi.me()
      .then((fresh) => { if (!cancelled) setSession(fresh); })         // refresh perms/role, keep token
      .catch(() => { if (!cancelled) setSession(null); })             // token invalid/expired -> sign out
      .finally(() => { if (!cancelled) setRevalidating(false); });
    return () => { cancelled = true; };
  }, []);

  if (revalidating) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-slate-300 border-t-teal-500" />
      </div>
    );
  }

  const value = useMemo(() => ({
    session,
    setSession,
    logout: async () => {
      try {
        // Revoke the server-side session (sends auth + CSRF via the shared client).
        if (session?.token) await authApi.logout();
      } catch {
        // Session may already be gone; clear local state regardless.
      } finally {
        setSession(null);
      }
    },
  }), [session]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
