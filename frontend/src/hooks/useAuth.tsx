import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import type { UserSession } from "@/types";

type AuthContextValue = {
  session: UserSession | null;
  setSession: (session: UserSession | null) => void;
  logout: () => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSessionState] = useState<UserSession | null>(() => {
    const stored = localStorage.getItem("opstrax.session");
    return stored ? JSON.parse(stored) : null;
  });

  const setSession = (next: UserSession | null) => {
    setSessionState(next);
    if (next) localStorage.setItem("opstrax.session", JSON.stringify(next));
    else localStorage.removeItem("opstrax.session");
  };

  const value = useMemo(() => ({
    session,
    setSession,
    logout: () => setSession(null),
  }), [session]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
