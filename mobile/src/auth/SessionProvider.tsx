/* eslint-disable react-hooks/refs, react-hooks/preserve-manual-memoization */
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { createMobileApiClient } from "@/api/client";
import { SECURE_SESSION_KEY } from "@/config";
import type { MobileSession, MobileSessionEnvelope } from "@/types";
import { classifyRole, type RoleModel, ROLE_MODELS } from "@/data/roleModel";

type SessionContextValue = {
  ready: boolean;
  session: MobileSession | null;
  authError: string | null;
  roleModel: RoleModel;
  normalizedRole: ReturnType<typeof classifyRole>;
  hasPermission: (permission: string) => boolean;
  api: ReturnType<typeof createMobileApiClient>;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refresh: () => Promise<void>;
};

const SessionContext = createContext<SessionContextValue | null>(null);

function normalizeSession(value: MobileSession | MobileSessionEnvelope | null | undefined): MobileSession | null {
  if (!value) return null;
  if ("session" in value) return value.session;
  return value;
}

export function SessionProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [session, setSessionState] = useState<MobileSession | null>(null);
  const [authError, setAuthError] = useState<string | null>(null);
  const sessionRef = useRef<MobileSession | null>(null);

  useEffect(() => {
    sessionRef.current = session;
  }, [session]);

  const setSession = (next: MobileSession | null) => {
    sessionRef.current = next;
    setSessionState(next);
    if (!next) {
      void SecureStore.deleteItemAsync(SECURE_SESSION_KEY);
    } else {
      void SecureStore.setItemAsync(SECURE_SESSION_KEY, JSON.stringify(next));
    }
  };

  const getSession = useCallback(() => sessionRef.current, []);

  const api = useMemo(
    () =>
      createMobileApiClient({
        getSession,
        setSession,
      }),
    [getSession],
  );

  useEffect(() => {
    void (async () => {
      try {
        const stored = await SecureStore.getItemAsync(SECURE_SESSION_KEY);
        if (!stored) {
          setReady(true);
          return;
        }

        const parsed = JSON.parse(stored) as MobileSession;
        if (!parsed?.token) {
          setReady(true);
          return;
        }

        sessionRef.current = parsed;
        setSessionState(parsed);
        const current = await api.me();
        const normalized = normalizeSession(current);
        if (normalized) {
          setSession(normalized);
        } else {
          setSession(null);
        }
      } catch (error) {
        setAuthError(error instanceof Error ? error.message : "Unable to restore session.");
        setSession(null);
      } finally {
        setReady(true);
      }
    })();
  }, [api]);

  const login = useCallback(async (email: string, password: string) => {
    setAuthError(null);
    const next = await api.login(email.trim(), password);
    const normalized = normalizeSession(next);
    if (!normalized) throw new Error("Login succeeded but no session was returned.");
    setSession(normalized);
  }, [api]);

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } catch {
      // Local logout must still clear the session even if the server is unavailable.
    } finally {
      setSession(null);
    }
  }, [api]);

  const refresh = useCallback(async () => {
    const next = await api.refresh();
    const normalized = normalizeSession(next);
    if (normalized) setSession(normalized);
  }, [api]);

  const normalizedRole = classifyRole(session?.role);
  const roleModel = ROLE_MODELS.find((entry) => entry.role === normalizedRole) ?? ROLE_MODELS.find((entry) => entry.role === "general")!;
  const hasPermission = useCallback(
    (permission: string) => Boolean(session?.permissions?.some((value) => value === "*" || value.toLowerCase() === permission.toLowerCase())),
    [session],
  );

  const value = useMemo(
    () => ({
      ready,
      session,
      authError,
      roleModel,
      normalizedRole,
      hasPermission,
      api,
      login,
      logout,
      refresh,
    }),
    [ready, session, authError, roleModel, normalizedRole, api, hasPermission, login, logout, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const context = useContext(SessionContext);
  if (!context) throw new Error("useSession must be used inside SessionProvider");
  return context;
}
