/* eslint-disable react-hooks/refs, react-hooks/preserve-manual-memoization */
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { createMobileApiClient } from "@/api/client";
import { SECURE_SESSION_KEY } from "@/config";
import type { LoginResult, MfaChallenge, MobileSession } from "@/types";
import { classifyRole, type RoleModel, ROLE_MODELS } from "@/data/roleModel";

type SessionContextValue = {
  ready: boolean;
  session: MobileSession | null;
  authError: string | null;
  mfaChallenge: MfaChallenge | null;
  roleModel: RoleModel;
  normalizedRole: ReturnType<typeof classifyRole>;
  hasPermission: (permission: string) => boolean;
  api: ReturnType<typeof createMobileApiClient>;
  login: (email: string, password: string, companyCode: string) => Promise<void>;
  verifyMfa: (code: string) => Promise<void>;
  cancelMfa: () => void;
  logout: () => Promise<void>;
  refresh: () => Promise<void>;
};

const SessionContext = createContext<SessionContextValue | null>(null);

const PERMISSION_ALIASES: Record<string, string[]> = {
  "dispatch.smart_assign.read": ["dispatch:view", "dispatch:manage", "dispatch:assign"],
  "dispatch.smart_assign.accept": ["dispatch:manage", "dispatch:assign"],
  "dispatch.smart_assign.reject": ["dispatch:manage", "dispatch:assign"],
  "operations.proof.read": ["dispatch:view", "dispatch:manage", "driver:self", "customer_portal:view"],
  "operations.proof.submit": ["operations.proof.create", "dispatch:manage", "driver:self"],
  "operations.proof.validate": ["dispatch:manage"],
  "operations.proof_artifact.read": ["operations.proof.read", "dispatch:view", "dispatch:manage", "driver:self", "customer_portal:view"],
};

function normalizeSession(value: LoginResult | null | undefined): MobileSession | null {
  if (!value) return null;
  const candidate = "session" in value ? value.session : value;
  return "token" in candidate && typeof candidate.token === "string" && candidate.token.length > 0
    ? candidate as MobileSession
    : null;
}

export function SessionProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [session, setSessionState] = useState<MobileSession | null>(null);
  const [authError, setAuthError] = useState<string | null>(null);
  const [mfaChallenge, setMfaChallenge] = useState<MfaChallenge | null>(null);
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
      void SecureStore.setItemAsync(SECURE_SESSION_KEY, JSON.stringify(next), {
        keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
      });
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

  const login = useCallback(async (email: string, password: string, companyCode: string) => {
    setAuthError(null);
    const next = await api.login(email.trim(), password, companyCode);
    if ("mfaRequired" in next && next.mfaRequired === true) {
      setMfaChallenge(next);
      return;
    }
    const normalized = normalizeSession(next);
    if (!normalized) throw new Error("Login succeeded but no session was returned.");
    setSession(normalized);
  }, [api]);

  const verifyMfa = useCallback(async (code: string) => {
    if (!mfaChallenge) throw new Error("Restart sign-in to request a new MFA challenge.");
    setAuthError(null);
    const next = await api.verifyMfaLogin(mfaChallenge.challengeToken, code.trim());
    const normalized = normalizeSession(next);
    if (!normalized) throw new Error("MFA verification succeeded but no session was returned.");
    setMfaChallenge(null);
    setSession(normalized);
  }, [api, mfaChallenge]);

  const cancelMfa = useCallback(() => setMfaChallenge(null), []);

  const logout = useCallback(async () => {
    await SecureStore.deleteItemAsync(SECURE_SESSION_KEY);
    setSessionState(null);
    setMfaChallenge(null);
    try {
      await api.logout();
    } catch {
      // Local logout must still clear the session even if the server is unavailable.
    } finally {
      sessionRef.current = null;
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
    (permission: string) => {
      const accepted = [permission, ...(PERMISSION_ALIASES[permission] ?? [])].map((value) => value.toLowerCase());
      return Boolean(session?.permissions?.some((value) => value === "*" || accepted.includes(value.toLowerCase())));
    },
    [session],
  );

  const value = useMemo(
    () => ({
      ready,
      session,
      authError,
      mfaChallenge,
      roleModel,
      normalizedRole,
      hasPermission,
      api,
      login,
      verifyMfa,
      cancelMfa,
      logout,
      refresh,
    }),
    [ready, session, authError, mfaChallenge, roleModel, normalizedRole, api, hasPermission, login, verifyMfa, cancelMfa, logout, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const context = useContext(SessionContext);
  if (!context) throw new Error("useSession must be used inside SessionProvider");
  return context;
}
