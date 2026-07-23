import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import type { UserSession } from "@/types";
import { authApi } from "@/services/authApi";

// v3: bumped when driver/role permission semantics changed (Driver role isolated to portal-only).
// Bumping the key auto-invalidates every cached session so a stale token with the old broad grants
// can never keep rendering surfaces it should no longer reach — everyone re-authenticates once and
// gets a fresh, correctly-scoped session. Retire older keys on load so they don't linger.
const STORAGE_KEY = "opstrax.session.v3";
const RETIRED_STORAGE_KEYS = ["opstrax.session.v2"];
const SESSION_TTL_MS = 8 * 60 * 60 * 1000; // 8 hours

type StoredSession = { session: UserSession; expiresAt: number };

function loadSession(): UserSession | null {
  try {
    for (const k of RETIRED_STORAGE_KEYS) localStorage.removeItem(k);
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
  const [session, setSessionState] = useState<UserSession | null>(loadSession);

  const setSession = (next: UserSession | null) => {
    setSessionState(next);
    if (next) {
      const stored: StoredSession = { session: next, expiresAt: Date.now() + SESSION_TTL_MS };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  };

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
