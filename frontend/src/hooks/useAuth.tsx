import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import type { UserSession } from "@/types";

const STORAGE_KEY = "opstrax.session.v2";
const SESSION_TTL_MS = 8 * 60 * 60 * 1000; // 8 hours

type StoredSession = { session: UserSession; expiresAt: number };

function loadSession(): UserSession | null {
  try {
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
        const baseUrl =
          import.meta.env.VITE_API_BASE_URL ||
          import.meta.env.VITE_DOTNET_API_URL ||
          "http://localhost:8088";
        if (session?.token) {
          await fetch(`${baseUrl}/api/auth/logout`, {
            method: "POST",
            headers: {
              Authorization: `Bearer ${session.token}`,
              Accept: "application/json",
            },
            credentials: "include",
          });
        }
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
