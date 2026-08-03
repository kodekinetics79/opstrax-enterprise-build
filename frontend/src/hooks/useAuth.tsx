import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { flushSync } from "react-dom";
import { useQueryClient } from "@tanstack/react-query";
import type { UserSession } from "@/types";
import { authApi } from "@/services/authApi";
import {
  SESSION_STORAGE_KEY as STORAGE_KEY,
  RETIRED_SESSION_KEYS,
  SESSION_KEY_LOOKUP,
  clearAllSessionKeys,
  readRawSession,
} from "@/auth/sessionStorage";
import { clearGlobalCsrfToken, setGlobalCsrfToken } from "@/auth/csrfTokenStore";

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
  const queryClient = useQueryClient();
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
      if (next.csrfToken) setGlobalCsrfToken(next.csrfToken);
      const stored: StoredSession = { session: next, expiresAt: Date.now() + SESSION_TTL_MS };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    } else {
      clearGlobalCsrfToken();
      clearAllSessionKeys();
      // React Query data is tenant-scoped by server authorization, but most query
      // keys intentionally omit company id. Never retain one tenant's rendered
      // records across logout and a subsequent login in the same document.
      queryClient.clear();
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

  // Logout/session replacement in another tab must invalidate this document too.
  // A storage event is untrusted input: validate any replacement with /me before
  // exposing it, and clear immediately when every known session key is gone.
  useEffect(() => {
    let validating = false;
    const onStorage = (event: StorageEvent) => {
      if (!event.key || !SESSION_KEY_LOOKUP.includes(event.key as (typeof SESSION_KEY_LOOKUP)[number])) return;
      if (!readRawSession()) {
        setSession(null);
        return;
      }
      if (validating) return;
      validating = true;
      authApi.me()
        // The originating tab already wrote the shared value. Update only this
        // document's memory or two tabs would continuously rewrite expiresAt.
        .then((fresh) => {
          queryClient.clear();
          setSessionState(fresh);
          if (fresh.csrfToken) setGlobalCsrfToken(fresh.csrfToken);
        })
        .catch(() => setSession(null))
        .finally(() => { validating = false; });
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  // A full-page navigation can leave an authenticated SPA document in the browser's
  // back/forward cache. If another document logs out, Back would otherwise restore that
  // old in-memory session and its already-rendered tenant data even though the server
  // session has been revoked. Hide the cached document before it is stored, then reveal
  // it only after /me has re-established current server authority (or cleared the session).
  useEffect(() => {
    let restoring = false;
    const onPageHide = (event: PageTransitionEvent) => {
      if (event.persisted) document.documentElement.style.visibility = "hidden";
    };
    const onPageShow = (event: PageTransitionEvent) => {
      if (!event.persisted || restoring) return;
      restoring = true;
      authApi.me()
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

  // Early return only AFTER every hook above has run (Rules of Hooks): gate the app on the one-time
  // session re-validation so stale permissions never render.
  if (revalidating) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-slate-300 border-t-teal-500" />
      </div>
    );
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
