import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import type { UserSession } from "@/types";
import { authApi } from "@/services/authApi";

const STORAGE_KEY = "opstrax.session.v2";
const SESSION_TTL_MS = 8 * 60 * 60 * 1000; // 8 hours

/**
 * DEV-ONLY bypass: auto-injects a super_admin session so the UI can be
 * tested without a running backend. Remove before production.
 */
const DEV_BYPASS_SESSION: UserSession = {
  token: "dev-bypass-token",
  csrfToken: "dev-bypass-csrf",
  user: { id: 1, name: "Dev Bypass", email: "dev@opstrax.local" },
  role: "super_admin",
  company: { id: "1", name: "OpsTrax Demo Logistics", plan: "Enterprise" },
  permissions: ["*"],
};

type StoredSession = { session: UserSession; expiresAt: number };

function loadSession(): UserSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return DEV_BYPASS_SESSION;
    const { session, expiresAt } = JSON.parse(raw) as StoredSession;
    if (Date.now() > expiresAt) {
      localStorage.removeItem(STORAGE_KEY);
      return DEV_BYPASS_SESSION;
    }
    return session;
  } catch {
    localStorage.removeItem(STORAGE_KEY);
    return DEV_BYPASS_SESSION;
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
